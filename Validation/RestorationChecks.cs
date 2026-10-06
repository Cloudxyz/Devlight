using System.Runtime.InteropServices;
using Devlight;

namespace DevlightValidation;

internal static partial class Validation
{
    private static void RestorationChecks()
    {
        var settings = new AppSettings { MonitorIdentity = "test-monitor", Brightness = 30 };
        int calls = 0;
        var delayedSuccess = new BrightnessRestorer((_, _) =>
        {
            int call = Interlocked.Increment(ref calls);
            return call < 3 ? Task.FromException(new InvalidOperationException("Monitor still waking")) : Task.CompletedTask;
        }, [TimeSpan.Zero, TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(15)]);
        delayedSuccess.Restore(settings);
        PumpUntil(() => Volatile.Read(ref calls) == 3);
        Thread.Sleep(60);
        Check(calls == 3, "temporary startup failures retry and stop after success");
        Complete(delayedSuccess.StopAsync());

        calls = 0;
        var exhausted = new BrightnessRestorer((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromException(new InvalidOperationException("Disconnected"));
        }, [TimeSpan.Zero, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)]);
        exhausted.Restore(settings);
        PumpUntil(() => Volatile.Read(ref calls) == 3);
        Thread.Sleep(50);
        Check(calls == 3, "unavailable monitor exhausts bounded retries without notifications");
        try { Complete(exhausted.ApplyManualAsync(settings)); throw new Exception("Expected manual failure"); }
        catch (InvalidOperationException) { Check(true, "manual errors propagate through the shared operation"); }
        Complete(exhausted.StopAsync());

        calls = 0;
        int active = 0, maximum = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var burst = new BrightnessRestorer(async (_, cancellation) =>
        {
            Interlocked.Increment(ref calls);
            int running = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, running);
            entered.TrySetResult();
            try { await release.Task; cancellation.ThrowIfCancellationRequested(); }
            finally { Interlocked.Decrement(ref active); }
        }, [TimeSpan.Zero]);
        burst.Restore(settings);
        Complete(entered.Task);
        Parallel.For(0, 100, _ => burst.Restore(settings));
        release.SetResult();
        Complete(burst.StopAsync());
        Check(calls == 1 && maximum == 1, "display/power event bursts coalesce into one operation");

        var targets = new List<int>();
        var staleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var superseded = new BrightnessRestorer(async (value, cancellation) =>
        {
            if (value.Brightness == 30) { staleEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); }
            lock (targets) targets.Add(value.Brightness);
        }, [TimeSpan.Zero]);
        superseded.Restore(settings);
        Complete(staleEntered.Task);
        Complete(superseded.ApplyManualAsync(settings with { Brightness = 45 }));
        Check(targets.SequenceEqual([45]), "manual apply cancels pending auto work without a stale write");
        superseded.Restore(settings with { Brightness = 70 });
        PumpUntil(() => { lock (targets) return targets.Count == 2; });
        Check(targets.SequenceEqual([45, 70]), "new settings replace the automatic target");
        Complete(superseded.StopAsync());

        calls = 0;
        var stopping = new BrightnessRestorer((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromException(new InvalidOperationException("Asleep"));
        }, [TimeSpan.Zero, TimeSpan.FromHours(1)]);
        stopping.Restore(settings);
        PumpUntil(() => Volatile.Read(ref calls) == 1);
        Complete(stopping.StopAsync());
        stopping.Restore(settings);
        Check(calls == 1, "exit cancels delayed retry and rejects further restoration");

        calls = 0;
        var invalid = new BrightnessRestorer((_, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        invalid.Restore(new AppSettings());
        Complete(invalid.StopAsync());
        Check(calls == 0, "invalid configuration starts no automatic operation");

        using var events = new DisplayEventWindow();
        Check(Field<IntPtr>(events, "_powerRegistration") != IntPtr.Zero
            && Field<IntPtr>(events, "_deviceRegistration") != IntPtr.Zero, "Windows power/monitor notification registrations succeed");
        int received = 0;
        events.AvailabilityChanged += () => received++;
        SendMessage(events.Handle, 0x007E, IntPtr.Zero, IntPtr.Zero);
        SendMessage(events.Handle, 0x0218, new IntPtr(0x12), IntPtr.Zero);
        SendMonitorArrival(events);
        SendMessage(events.Handle, 0x0219, new IntPtr(0x0007), IntPtr.Zero);
        Check(received == 4, "display, resume, reconnect and topology messages reach event detection");
        SendMessage(events.Handle, 0x0219, new IntPtr(0x8000), IntPtr.Zero);
        Check(received == 4, "unrelated device arrival does not trigger restoration");
        IntPtr data = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(DisplayEventWindow.SessionDisplayStatus, data, false);
            Marshal.WriteInt32(data, 16, 4);
            Marshal.WriteInt32(data, 20, 0);
            SendMessage(events.Handle, 0x0218, new IntPtr(0x8013), data);
            Check(received == 4, "display-off notification does not start restoration");
            Marshal.WriteInt32(data, 20, 1);
            SendMessage(events.Handle, 0x0218, new IntPtr(0x8013), data);
            Check(received == 5, "session display-on notification starts restoration");
        }
        finally { Marshal.FreeHGlobal(data); }
        events.Dispose();
        Check(events.Handle == IntPtr.Zero, "event window disposes registrations and native handle");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void SendMonitorArrival(DisplayEventWindow events)
    {
        var notification = new NativeMethods.DeviceInterfaceFilter
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.DeviceInterfaceFilter>(), DeviceType = 5,
            ClassGuid = new Guid("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7")
        };
        IntPtr data = Marshal.AllocHGlobal((int)notification.Size);
        try
        {
            Marshal.StructureToPtr(notification, data, false);
            SendMessage(events.Handle, 0x0219, new IntPtr(0x8000), data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
