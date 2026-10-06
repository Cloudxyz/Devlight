using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Devlight;

internal sealed record MonitorChoice(string Identity, string Name, bool Supported, string? Problem)
{
    public override string ToString() => Supported ? Name : $"{Name} — {Problem}";
}

internal static class MonitorIdentity
{
    // Device interface paths survive enumeration reordering. Never guess from a name or index.
    internal static bool Matches(string saved, string current) =>
        StringComparer.OrdinalIgnoreCase.Equals(saved, current);
}

internal sealed class MonitorService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _stopping;

    internal Task<List<MonitorChoice>> DiscoverAsync() => RunAsync(() =>
    {
        using var snapshot = Capture();
        return snapshot.Monitors.Select(m =>
        {
            if (m.Handle is null) return new MonitorChoice(m.Identity, m.Name, false, m.Problem ?? "Physical monitor handle unavailable");
            bool supported = TryBrightness(m.Handle.Value, out _, out _, out string? problem, queryCapabilities: true);
            return new MonitorChoice(m.Identity, m.Name, supported, problem);
        }).ToList();
    });

    internal Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var snapshot = Capture();
        var matches = snapshot.Monitors.Where(m => MonitorIdentity.Matches(settings.MonitorIdentity!, m.Identity)).ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException("Your saved monitor is unavailable. Wake or reconnect it, or select it in Settings.");
        if (matches.Count != 1 || matches[0].Handle is null)
            throw new InvalidOperationException(matches.FirstOrDefault()?.Problem ?? "This monitor cannot be identified safely. Open Settings.");
        var monitor = matches[0];
        if (!TryBrightness(monitor.Handle!.Value, out uint min, out uint max, out string? problem))
            throw new InvalidOperationException($"{monitor.Name}: {problem}. Wake the monitor and enable DDC/CI in its OSD.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.SetMonitorBrightness(monitor.Handle.Value, ToNativeBrightness(settings.Brightness, min, max)))
        {
            LogNative("SetMonitorBrightness");
            throw new InvalidOperationException("The monitor did not accept brightness. Wake it, check DDC/CI, and try again.");
        }
        return true;
    }, cancellationToken);

    internal static uint ToNativeBrightness(int percentage, uint minimum, uint maximum)
    {
        if (maximum <= minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        return minimum + (uint)Math.Round((maximum - (double)minimum) * Math.Clamp(percentage, 0, 100) / 100,
            MidpointRounding.AwayFromZero);
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopping) throw new OperationCanceledException();
            return await Task.Run(action, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task StopAsync()
    {
        _stopping = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private static bool TryBrightness(IntPtr handle, out uint min, out uint max, out string? problem, bool queryCapabilities = false)
    {
        // Some monitors misreport capability bits. A successful brightness read is definitive.
        // Capability requests can be slow; reserve them for Settings discovery.
        if (queryCapabilities)
        {
            if (!NativeMethods.GetMonitorCapabilities(handle, out uint caps, out _)) LogNative("GetMonitorCapabilities");
            else Debug.WriteLine($"Monitor capabilities: 0x{caps:X}");
        }
        if (!NativeMethods.GetMonitorBrightness(handle, out min, out _, out max))
        {
            LogNative("GetMonitorBrightness");
            problem = "Brightness unavailable (DDC/CI disabled, unsupported, or asleep)";
            return false;
        }
        problem = max > min ? null : "Invalid brightness range";
        return problem is null;
    }

    private sealed record Candidate(string Identity, string Name, IntPtr? Handle, string? Problem);

    private sealed class Snapshot : IDisposable
    {
        internal List<(NativeMethods.PhysicalMonitor[] Monitors, bool Complete)> Handles { get; } = [];
        internal List<Candidate> Monitors { get; } = [];
        public void Dispose()
        {
            foreach (var group in Handles)
                foreach (var monitor in group.Monitors)
                    // Physical-monitor tokens are opaque: zero can be valid on successful acquisition.
                    if ((group.Complete || monitor.Handle != IntPtr.Zero) && !NativeMethods.DestroyPhysicalMonitor(monitor.Handle))
                        LogNative("DestroyPhysicalMonitor");
            Handles.Clear();
        }
    }

    private static Snapshot Capture()
    {
        var snapshot = new Snapshot();
        try
        {
            var friendlyNames = GetFriendlyNames();
            // Collect logical handles first: managed exceptions must never escape a native callback.
            var logical = new List<IntPtr>();
            NativeMethods.MonitorCallback callback = (IntPtr h, IntPtr dc, ref NativeMethods.Rect rect, IntPtr data) =>
            { logical.Add(h); return true; };
            if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw NativeError("Windows could not enumerate monitors.");
            foreach (var handle in logical)
            {
                var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
                if (!NativeMethods.GetMonitorInfo(handle, ref info))
                    throw NativeError("Windows could not read monitor information. Try again.");
                var devices = new List<NativeMethods.DisplayDevice>();
                for (uint i = 0; ; i++)
                {
                    var device = new NativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>() };
                    if (!NativeMethods.EnumDisplayDevices(info.Device, i, ref device, 1)) break;
                    if ((device.Flags & 1) != 0) devices.Add(device);
                }
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out uint count))
                {
                    LogNative("GetNumberOfPhysicalMonitorsFromHMONITOR");
                    AddUnavailable(snapshot, devices, "Physical monitor unavailable");
                    continue;
                }
                if (count == 0)
                {
                    AddUnavailable(snapshot, devices, "No DDC/CI physical monitor exposed");
                    continue;
                }
                var physical = new NativeMethods.PhysicalMonitor[count];
                bool acquired = NativeMethods.GetPhysicalMonitorsFromHMONITOR(handle, count, physical);
                snapshot.Handles.Add((physical, acquired)); // Own even partially filled output on native failure.
                if (!acquired)
                {
                    LogNative("GetPhysicalMonitorsFromHMONITOR");
                    AddUnavailable(snapshot, devices, "Physical monitor handle unavailable");
                    continue;
                }
                // Windows does not specify an association/order between multiple physical handles and
                // interface names. Refuse ambiguous clone/tiled groups instead of changing the wrong screen.
                if (devices.Count != 1 || physical.Length != 1 || string.IsNullOrWhiteSpace(devices[0].Identity))
                {
                    AddUnavailable(snapshot, devices, "Ambiguous monitor mapping (use extended displays)");
                    continue;
                }
                var selected = devices[0];
                string name = friendlyNames.GetValueOrDefault(selected.Identity)
                    ?? (string.IsNullOrWhiteSpace(physical[0].Description) ? selected.Description : physical[0].Description);
                snapshot.Monitors.Add(new Candidate(selected.Identity, $"{name} ({info.Device})", physical[0].Handle, null));
            }
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static void AddUnavailable(Snapshot snapshot, List<NativeMethods.DisplayDevice> devices, string problem)
    {
        foreach (var device in devices)
            snapshot.Monitors.Add(new Candidate(device.Identity ?? "", device.Description, null, problem));
    }

    private static Dictionary<string, string> GetFriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const uint activePaths = 2;
        // Topology can change between sizing and querying. Retry ERROR_INSUFFICIENT_BUFFER.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int error = NativeMethods.GetDisplayConfigBufferSizes(activePaths, out uint pathCount, out uint modeCount);
            if (error != 0) { Debug.WriteLine(new Win32Exception(error)); break; }
            var paths = new NativeMethods.DisplayPath[pathCount];
            var modes = new NativeMethods.DisplayMode[modeCount];
            error = NativeMethods.QueryDisplayConfig(activePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == 122) continue;
            if (error != 0) { Debug.WriteLine(new Win32Exception(error)); break; }
            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new NativeMethods.TargetDeviceName
                {
                    Header = new NativeMethods.DeviceInfoHeader
                    {
                        Type = 2, Size = (uint)Marshal.SizeOf<NativeMethods.TargetDeviceName>(),
                        Adapter = path.Target.Adapter, Id = path.Target.Id
                    }
                };
                error = NativeMethods.GetTargetDeviceName(ref target);
                if (error != 0) { Debug.WriteLine(new Win32Exception(error)); continue; }
                if (!string.IsNullOrWhiteSpace(target.DevicePath) && !string.IsNullOrWhiteSpace(target.FriendlyName))
                    names[target.DevicePath] = target.FriendlyName;
            }
            break;
        }
        return names;
    }

    private static Exception NativeError(string message)
    {
        LogNative(message);
        return new InvalidOperationException(message);
    }

    private static void LogNative(string operation) => Debug.WriteLine($"{operation}: {new Win32Exception(Marshal.GetLastWin32Error())}");
}
