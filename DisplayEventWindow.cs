using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Devlight;

// A hidden top-level native window receives broadcasts; message-only windows do not.
internal sealed class DisplayEventWindow : NativeWindow, IDisposable
{
    internal static readonly Guid SessionDisplayStatus = new("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
    private static readonly Guid MonitorInterface = new("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7");
    private IntPtr _powerRegistration;
    private IntPtr _deviceRegistration;
    private bool _disposed;
    internal event Action? AvailabilityChanged;

    internal DisplayEventWindow()
    {
        CreateHandle(new CreateParams { Caption = "Devlight display events" });
        var powerGuid = SessionDisplayStatus;
        _powerRegistration = NativeMethods.RegisterPowerSettingNotification(Handle, ref powerGuid, 0);
        if (_powerRegistration == IntPtr.Zero) LogNative("RegisterPowerSettingNotification");
        var filter = new NativeMethods.DeviceInterfaceFilter
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.DeviceInterfaceFilter>(), DeviceType = 5, ClassGuid = MonitorInterface
        };
        _deviceRegistration = NativeMethods.RegisterDeviceNotification(Handle, ref filter, 0);
        if (_deviceRegistration == IntPtr.Zero) LogNative("RegisterDeviceNotification");
    }

    protected override void WndProc(ref Message message)
    {
        bool relevant = message.Msg == 0x007E; // WM_DISPLAYCHANGE
        if (message.Msg == 0x0219) // WM_DEVICECHANGE: topology/monitor device arrival or removal
        {
            long kind = message.WParam.ToInt64();
            relevant = kind == 0x0007;
            if (kind is 0x8000 or 0x8004 && message.LParam != IntPtr.Zero)
                relevant = Marshal.ReadInt32(message.LParam) >= 28 && Marshal.ReadInt32(message.LParam, 4) == 5
                    && Marshal.PtrToStructure<Guid>(IntPtr.Add(message.LParam, 12)) == MonitorInterface;
        }
        if (message.Msg == 0x0218) // WM_POWERBROADCAST
        {
            long kind = message.WParam.ToInt64();
            relevant = kind is 0x0006 or 0x0007 or 0x0012; // resume critical, suspend, automatic
            if (kind == 0x8013 && message.LParam != IntPtr.Zero)
                relevant = Marshal.PtrToStructure<Guid>(message.LParam) == SessionDisplayStatus
                    && Marshal.ReadInt32(message.LParam, 16) == 4 && Marshal.ReadInt32(message.LParam, 20) == 1;
        }
        if (relevant && !_disposed) AvailabilityChanged?.Invoke();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AvailabilityChanged = null;
        if (_powerRegistration != IntPtr.Zero && !NativeMethods.UnregisterPowerSettingNotification(_powerRegistration))
            LogNative("UnregisterPowerSettingNotification");
        if (_deviceRegistration != IntPtr.Zero && !NativeMethods.UnregisterDeviceNotification(_deviceRegistration))
            LogNative("UnregisterDeviceNotification");
        DestroyHandle();
    }

    private static void LogNative(string operation) => Trace.WriteLine($"{operation}: {new Win32Exception(Marshal.GetLastWin32Error())}");
}
