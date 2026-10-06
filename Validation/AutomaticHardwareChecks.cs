using System.Reflection;
using Devlight;

namespace DevlightValidation;

internal static partial class Validation
{
    private static void WaitForRestoration(TrayApplicationContext context) =>
        PumpUntil(() => Field<Task?>(Field<BrightnessRestorer>(context, "_restorer"), "_worker") is null);

    private static void AutomaticHardwareChecks(TrayApplicationContext context, IntPtr handle, MonitorChoice monitor,
        uint min, uint max, uint original, int savedPercent)
    {
        uint saved = MonitorService.ToNativeBrightness(savedPercent, min, max);
        var events = Field<DisplayEventWindow>(context, "_displayEvents");
        foreach (var trigger in new[] { (Message: 0x007Eu, Kind: 0L, Name: "display-change"),
                     (Message: 0x0218u, Kind: 0x12L, Name: "resume"), (Message: 0x0219u, Kind: 0x8000L, Name: "monitor-arrival") })
        {
            Check(NativeMethods.SetMonitorBrightness(handle, original) && ReadUntil(handle, original), $"reset hardware before {trigger.Name} event");
            if (trigger.Message == 0x0219) SendMonitorArrival(events);
            else SendMessage(events.Handle, trigger.Message, new IntPtr(trigger.Kind), IntPtr.Zero);
            WaitForRestoration(context);
            Check(ReadUntil(handle, saved), $"{trigger.Name} message automatically restores real hardware brightness");
        }

        typeof(TrayApplicationContext).GetMethod("ShowSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null);
        var form = Application.OpenForms.Cast<Form>().Single();
        var combo = Field<ComboBox>(form, "_monitors");
        PumpUntil(() => combo.Items.Count > 0);
        combo.SelectedItem = combo.Items.Cast<MonitorChoice>().Single(m => MonitorIdentity.Matches(m.Identity, monitor.Identity));
        int nextPercent = savedPercent == 32 ? 33 : 32;
        Field<NumericUpDown>(form, "_brightness").Value = nextPercent;
        Field<Button>(form, "_save").PerformClick();
        WaitForRestoration(context);
        uint next = MonitorService.ToNativeBrightness(nextPercent, min, max);
        Check(Application.OpenForms.Count == 0 && ReadUntil(handle, next), "Settings Save immediately applies the new hardware brightness");
        Check(NativeMethods.SetMonitorBrightness(handle, original) && ReadUntil(handle, original), "reset hardware before restoring changed settings");
        SendMessage(events.Handle, 0x0218, new IntPtr(0x12), IntPtr.Zero);
        WaitForRestoration(context);
        Check(ReadUntil(handle, next), "future resume restoration uses newly saved brightness");

        var persist = typeof(TrayApplicationContext).GetMethod("Persist", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Check((bool)persist.Invoke(context, [new AppSettings { MonitorIdentity = "validation-disconnected", Brightness = 30 }])!, "save unavailable monitor for pending-exit check");
        Thread.Sleep(100);
        var restorer = Field<BrightnessRestorer>(context, "_restorer");
        Check(Field<Task?>(restorer, "_worker") is not null, "unavailable monitor has a pending bounded restoration");
        Complete((Task)typeof(TrayApplicationContext).GetMethod("ExitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(context, null)!);
        Check(!Field<NotifyIcon>(context, "_tray").Visible && Field<Task?>(restorer, "_worker") is null,
            "application Exit cancels pending restoration and removes tray icon");
    }
}
