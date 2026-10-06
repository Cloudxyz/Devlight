using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Devlight;

namespace DevlightValidation;

internal static partial class Validation
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string directory = Path.Combine(Path.GetTempPath(), $"Devlight-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            RestorationChecks();
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            Check(!store.Load().IsValid, "missing settings");
            foreach (string content in new[] { "", "{", "null", "{\"Version\":99}", "{\"MonitorIdentity\":\"path\",\"Brightness\":101}" })
            {
                File.WriteAllText(store.FilePath, content);
                Check(!store.Load().IsValid, $"invalid settings: {content}");
            }
            var settings = new AppSettings { MonitorIdentity = @"\\?\DISPLAY#example", MonitorName = "Example", Brightness = 30, StartWithWindows = true };
            store.Save(settings);
            Check(store.Load() == settings, "settings round trip");
            store.Save(settings with { Brightness = 70 });
            Check(store.Load().Brightness == 70, "atomic replacement");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "save temporary files cleaned");
            Check(MonitorIdentity.Matches("ABC", "abc") && !MonitorIdentity.Matches("ABC", "XYZ"), "identity matching");
            Check(MonitorService.ToNativeBrightness(30, 20, 220) == 80, "native brightness translation");
            Check(MonitorService.ToNativeBrightness(-50, 20, 220) == 20 && MonitorService.ToNativeBrightness(150, 20, 220) == 220, "brightness clamping");
            Check(MonitorService.ToNativeBrightness(100, 0, uint.MaxValue) == uint.MaxValue, "large native range");
            var service = new MonitorService();
            var monitors = Complete(service.DiscoverAsync());
            Console.WriteLine($"Windows discovery: {monitors.Count} monitors, {monitors.Count(m => m.Supported)} controllable");
            foreach (var monitor in monitors) Console.WriteLine($"  {monitor} | {monitor.Identity}");
            try
            {
                Complete(service.ApplyAsync(settings with { MonitorIdentity = "devlight-validation-disconnected-monitor" }));
                throw new Exception("Disconnected monitor unexpectedly applied.");
            }
            catch (InvalidOperationException) { Check(true, "disconnected monitor handled"); }
            Complete(service.StopAsync());
            if (args.Contains("--desktop")) DesktopChecks(settings);
            int processOption = Array.IndexOf(args, "--process");
            if (args.Contains("--hardware") || args.Contains("--auto-hardware"))
                HardwareChecks(monitors.First(m => m.Supported), args.Contains("--auto-hardware"),
                    processOption >= 0 ? Path.GetFullPath(args[processOption + 1]) : null);
            if (processOption >= 0) ProcessChecks(Path.GetFullPath(args[processOption + 1]));
            Console.WriteLine($"PASS: {_checks} checks. Hardware writes: {args.Contains("--hardware") || args.Contains("--auto-hardware")}.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { Directory.Delete(directory, true); }
    }

    private static void DesktopChecks(AppSettings settings)
    {
        var store = new SettingsStore();
        var startup = new StartupRegistration();
        byte[]? original = File.Exists(store.FilePath) ? File.ReadAllBytes(store.FilePath) : null;
        string? originalStartup = startup.Read();
        try
        {
            if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
            using (var context = new TrayApplicationContext())
            {
                Application.RaiseIdle(EventArgs.Empty);
                PumpUntil(() => Application.OpenForms.Count > 0);
                var form = Application.OpenForms.Cast<Form>().Single();
                Check(form.Visible && !form.ShowInTaskbar, "first run settings without taskbar window");
                Check(Field<NotifyIcon>(context, "_tray").Visible, "tray icon registered");
                ((Button)form.CancelButton!).PerformClick();
                Check(Application.OpenForms.Count == 0 && Field<NotifyIcon>(context, "_tray").Visible, "cancel leaves tray only");
                typeof(TrayApplicationContext).GetMethod("ShowSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null);
                form = Application.OpenForms.Cast<Form>().Single();
                var combo = Field<ComboBox>(form, "_monitors");
                PumpUntil(() => combo.Items.Count > 0);
                var compatible = combo.Items.Cast<MonitorChoice>().Where(m => m.Supported).ToList();
                if (compatible.Count > 0)
                {
                    var fakeFirst = new MonitorChoice("validation-disconnected-one", "Validation monitor one", true, null);
                    combo.Items.Add(fakeFirst);
                    combo.SelectedItem = fakeFirst;
                    Field<NumericUpDown>(form, "_brightness").Value = 31;
                    Field<Button>(form, "_save").PerformClick();
                    Check(store.Load().MonitorIdentity == fakeFirst.Identity && store.Load().Brightness == 31
                        && Application.OpenForms.Count == 0, "settings save monitor/brightness and close");
                    typeof(TrayApplicationContext).GetMethod("ShowSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null);
                    form = Application.OpenForms.Cast<Form>().Single();
                    combo = Field<ComboBox>(form, "_monitors");
                    PumpUntil(() => combo.Items.Count > 0);
                    var next = new MonitorChoice("validation-disconnected-two", "Validation monitor two", true, null);
                    combo.Items.Add(next);
                    combo.SelectedItem = next;
                    Field<NumericUpDown>(form, "_brightness").Value = 72;
                    Field<Button>(form, "_save").PerformClick();
                    Check(store.Load().MonitorIdentity == next.Identity && store.Load().Brightness == 72, "settings changes saved monitor and brightness");
                }
                Complete((Task)typeof(TrayApplicationContext).GetMethod("ExitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null)!);
            }
            startup.Set(true);
            Check(startup.Read() == $"\"{Environment.ProcessPath}\"", "startup enabled with quoted executable path");
            startup.Set(false);
            Check(startup.Read() is null, "startup disabled");
            store.Save(settings with { StartWithWindows = false });
            using (var context = new TrayApplicationContext())
            {
                Application.DoEvents();
                Check(Application.OpenForms.Count == 0 && Field<NotifyIcon>(context, "_tray").Visible, "restart with settings stays tray only");
                var method = typeof(TrayApplicationContext).GetMethod("ApplyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Complete((Task)method.Invoke(context, null)!);
                Check(Field<NotifyIcon>(context, "_tray").Visible, "unavailable saved monitor keeps tray running");
                Complete((Task)typeof(TrayApplicationContext).GetMethod("ExitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null)!);
            }
        }
        finally
        {
            startup.Restore(originalStartup);
            if (original is null) { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); }
            else File.WriteAllBytes(store.FilePath, original);
        }
    }

    private static void HardwareChecks(MonitorChoice choice, bool automatic = false, string? executable = null)
    {
        var store = new SettingsStore();
        var startup = new StartupRegistration();
        string? originalStartup = startup.Read();
        byte[]? originalSettings = File.Exists(store.FilePath) ? File.ReadAllBytes(store.FilePath) : null;
        var logical = new List<IntPtr>();
        NativeMethods.MonitorCallback callback = (IntPtr h, IntPtr dc, ref NativeMethods.Rect r, IntPtr d) => { logical.Add(h); return true; };
        Check(NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero), "hardware logical enumeration");
        NativeMethods.PhysicalMonitor[]? acquired = null;
        uint originalBrightness = 0;
        bool restoreBrightness = false;
        try
        {
            foreach (var handle in logical)
            {
                var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
                if (!NativeMethods.GetMonitorInfo(handle, ref info)) continue;
                var device = new NativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>() };
                if (!NativeMethods.EnumDisplayDevices(info.Device, 0, ref device, 1)
                    || !MonitorIdentity.Matches(choice.Identity, device.Identity)) continue;
                Check(NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out uint count) && count == 1, "hardware unambiguous physical monitor");
                var physical = new NativeMethods.PhysicalMonitor[count];
                Check(NativeMethods.GetPhysicalMonitorsFromHMONITOR(handle, count, physical), "hardware handle acquisition");
                acquired = physical;
                break;
            }
            if (acquired is null) throw new Exception("Selected monitor disappeared.");
            IntPtr nativeHandle = acquired[0].Handle;
            Check(NativeMethods.GetMonitorBrightness(nativeHandle, out uint min, out originalBrightness, out uint max), "hardware original brightness read");
            Console.WriteLine($"Hardware baseline: handle={nativeHandle}, range={min}..{max}, current={originalBrightness}");
            int percent = MonitorService.ToNativeBrightness(30, min, max) == originalBrightness ? 31 : 30;
            uint expected = MonitorService.ToNativeBrightness(percent, min, max);
            restoreBrightness = true;
            store.Save(new AppSettings { MonitorIdentity = choice.Identity, MonitorName = choice.Name, Brightness = percent });
            using (var context = new TrayApplicationContext())
            {
                Application.RaiseIdle(EventArgs.Empty);
                WaitForRestoration(context);
                if (automatic)
                {
                    Check(ReadUntil(nativeHandle, expected), "startup automatically restores saved brightness without a click");
                    Check(NativeMethods.SetMonitorBrightness(nativeHandle, originalBrightness) && ReadUntil(nativeHandle, originalBrightness), "change hardware brightness before manual click");
                }
                var tray = Field<NotifyIcon>(context, "_tray");
                var mouseClick = typeof(NotifyIcon).GetMethod("OnMouseClick", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingMethodException("NotifyIcon.OnMouseClick");
                mouseClick.Invoke(tray, [new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)]);
                PumpUntil(() => !Field<bool>(context, "_applying"));
                Check(ReadUntil(nativeHandle, expected),
                    $"tray left-click handler hardware readback: {choice.Name}, {originalBrightness} -> {expected} (target {percent}%)");
                Check(Application.OpenForms.Count == 0, "hardware apply opens no dialog");
                if (automatic) AutomaticHardwareChecks(context, nativeHandle, choice, min, max, originalBrightness, percent);
                Complete((Task)typeof(TrayApplicationContext).GetMethod("ExitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null)!);
            }
            if (automatic && executable is not null)
            {
                store.Save(new AppSettings { MonitorIdentity = choice.Identity, MonitorName = choice.Name, Brightness = percent });
                Check(NativeMethods.SetMonitorBrightness(nativeHandle, originalBrightness) && ReadUntil(nativeHandle, originalBrightness), "change brightness while Devlight is closed");
                using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
                try
                {
                    Check(ReadUntil(nativeHandle, expected), "actual portable process startup restores saved hardware brightness");
                    Check(!process.HasExited && VisibleWindows(process.Id).Count == 0, "actual automatic startup remains tray only");
                }
                finally
                {
                    uint thread = (uint)process.Threads.Cast<ProcessThread>().OrderBy(t => t.StartTime).First().Id;
                    PostThreadMessage(thread, 0x12, IntPtr.Zero, IntPtr.Zero);
                    if (!process.WaitForExit(10000)) process.Kill();
                }
            }
        }
        catch (Exception exception) { Console.Error.WriteLine($"Hardware test failed before cleanup: {exception}"); throw; }
        finally
        {
            try
            {
                if (acquired is not null)
                {
                    try
                    {
                        if (restoreBrightness)
                        {
                            Check(NativeMethods.SetMonitorBrightness(acquired[0].Handle, originalBrightness), "hardware original brightness restored");
                            Check(ReadUntil(acquired[0].Handle, originalBrightness), "hardware restore readback");
                        }
                    }
                    finally { foreach (var monitor in acquired) NativeMethods.DestroyPhysicalMonitor(monitor.Handle); }
                }
            }
            finally
            {
                startup.Restore(originalStartup);
                if (originalSettings is null) { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); }
                else File.WriteAllBytes(store.FilePath, originalSettings);
            }
        }
    }

    private static bool ReadUntil(IntPtr handle, uint expected)
    {
        for (int i = 0; i < 25; i++)
        {
            if (NativeMethods.GetMonitorBrightness(handle, out _, out uint actual, out _) && actual == expected) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private static void ProcessChecks(string executable)
    {
        var store = new SettingsStore();
        byte[]? original = File.Exists(store.FilePath) ? File.ReadAllBytes(store.FilePath) : null;
        Process? process = null;
        uint uiThread = 0;
        try
        {
            if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
            process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
            List<IntPtr> windows = [];
            PumpUntil(() => { windows = VisibleWindows(process.Id); return windows.Count > 0 || process.HasExited; });
            Check(!process.HasExited && windows.Count == 1, "portable first run opens one settings window");
            IntPtr settingsWindow = windows[0];
            long extendedStyle = GetWindowLongPtr(settingsWindow, -20).ToInt64();
            Check((extendedStyle & 0x40000) == 0 && ((extendedStyle & 0x80) != 0 || GetWindow(settingsWindow, 4) != IntPtr.Zero),
                "portable settings excluded from taskbar");
            uiThread = GetWindowThreadProcessId(settingsWindow, out _);
            using (var duplicate = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!)
                Check(duplicate.WaitForExit(5000) && duplicate.ExitCode == 0 && !process.HasExited, "portable second instance exits without duplicate tray");
            PostMessage(settingsWindow, 0x10, IntPtr.Zero, IntPtr.Zero);
            PumpUntil(() => VisibleWindows(process.Id).Count == 0);
            Check(!process.HasExited, "portable closing settings leaves tray process running");
            // Let discovery finish; WM_QUIT exits the normal message loop and disposes its context.
            Thread.Sleep(3000);
            PostThreadMessage(uiThread, 0x12, IntPtr.Zero, IntPtr.Zero);
            Check(process.WaitForExit(10000) && process.ExitCode == 0, "portable message loop exits cleanly");
            process.Dispose();
            process = null;

            store.Save(new AppSettings { MonitorIdentity = "validation-disconnected", Brightness = 30 });
            process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
            PumpUntil(() => process.Threads.Count > 0);
            Thread.Sleep(1500);
            Check(!process.HasExited && VisibleWindows(process.Id).Count == 0, "portable restart with config has no visible window");
            // GDI+ may create other hidden windows on worker threads. WinForms runs on Main.
            uiThread = (uint)process.Threads.Cast<ProcessThread>().OrderBy(t => t.StartTime).First().Id;
            PostThreadMessage(uiThread, 0x12, IntPtr.Zero, IntPtr.Zero);
            Check(process.WaitForExit(10000) && process.ExitCode == 0, "portable tray-only process exits cleanly");
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) { PostThreadMessage(uiThread, 0x12, IntPtr.Zero, IntPtr.Zero); if (!process.WaitForExit(5000)) process.Kill(); }
                process.Dispose();
            }
            if (original is null) { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); }
            else File.WriteAllBytes(store.FilePath, original);
        }
    }

    private static List<IntPtr> VisibleWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((IntPtr window, IntPtr data) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    private static T Complete<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        do { Application.DoEvents(); Thread.Sleep(10); if (DateTime.UtcNow > deadline) throw new TimeoutException(); }
        while (!condition());
    }
    private static void Check(bool success, string name)
    {
        if (!success) throw new Exception($"FAIL: {name}");
        _checks++;
        Console.WriteLine($"PASS: {name}");
    }
}
