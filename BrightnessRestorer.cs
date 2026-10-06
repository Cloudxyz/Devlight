using System.Diagnostics;

namespace Devlight;

// One worker and one replaceable pending request, shared by automatic and manual actions.
internal sealed class BrightnessRestorer
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5),
         TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)];
    private readonly object _sync = new();
    private readonly Func<AppSettings, CancellationToken, Task> _apply;
    private readonly TimeSpan[] _delays;
    private Task? _worker;
    private Request? _pending;
    private Request? _active;
    private CancellationTokenSource? _activeCancellation;
    private bool _stopping;

    private sealed record Request(AppSettings Settings, TaskCompletionSource? Completion);

    internal BrightnessRestorer(Func<AppSettings, CancellationToken, Task> apply, TimeSpan[]? delays = null)
    {
        _apply = apply;
        _delays = delays ?? RetryDelays;
    }

    internal void Restore(AppSettings settings) => Queue(new Request(settings, null));

    internal Task ApplyManualAsync(AppSettings settings)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(new Request(settings, completion));
        return completion.Task;
    }

    private void Queue(Request request)
    {
        lock (_sync)
        {
            if (_stopping || !request.Settings.IsValid) { request.Completion?.TrySetCanceled(); return; }
            // Event bursts for the same target join the sequence already in progress.
            if (request.Completion is null && (_pending ?? _active) is { } existing
                && SameTarget(existing.Settings, request.Settings)) return;
            _pending?.Completion?.TrySetCanceled();
            _pending = request;
            _activeCancellation?.Cancel();
            _worker ??= Task.Run(PumpAsync);
        }
    }

    private static bool SameTarget(AppSettings left, AppSettings right) =>
        MonitorIdentity.Matches(left.MonitorIdentity!, right.MonitorIdentity!) && left.Brightness == right.Brightness;

    private async Task PumpAsync()
    {
        while (true)
        {
            Request request;
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_stopping || _pending is null) { _worker = null; return; }
                request = _pending;
                _pending = null;
                _active = request;
                cancellation = _activeCancellation = new CancellationTokenSource();
            }
            try
            {
                int attempts = request.Completion is null ? _delays.Length : 1;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (request.Completion is null && _delays[attempt] > TimeSpan.Zero)
                        await Task.Delay(_delays[attempt], cancellation.Token).ConfigureAwait(false);
                    try
                    {
                        await _apply(request.Settings, cancellation.Token).ConfigureAwait(false);
                        request.Completion?.TrySetResult();
                        break; // No timer or checks remain once brightness is restored.
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                    catch (Exception exception) when (request.Completion is null)
                    { Trace.WriteLine($"Automatic brightness attempt {attempt + 1}: {exception}"); }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { request.Completion?.TrySetCanceled(); }
            catch (Exception exception) { request.Completion?.TrySetException(exception); }
            finally
            {
                lock (_sync)
                {
                    _active = null;
                    _activeCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    internal Task StopAsync()
    {
        lock (_sync)
        {
            _stopping = true;
            _pending?.Completion?.TrySetCanceled();
            _pending = null;
            _activeCancellation?.Cancel();
            return _worker ?? Task.CompletedTask;
        }
    }
}
