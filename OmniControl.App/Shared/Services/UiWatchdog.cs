namespace OmniControl.App.Shared.Services;

/// Watches that the UI thread keeps answering: a worker posts a heartbeat every <see cref="Timing.UiHeartbeatEvery"/>, and when the UI
/// thread has left one unrun for <see cref="Timing.UiStuckAfter"/> or more (seen at the next look) the stall and its length are written once to <see cref="LogFile"/> (no
/// thread stacks: reading them needed a debugging library that doubled the download). One log per stall, at most
/// <see cref="Timing.UiStuckLogsPerRun"/> per run; it never acts on the stall, it only records it.
public sealed class UiWatchdog : IDisposable
{
    public const string LogFile = "hang-log.txt";

    private readonly Thread _worker;
    private readonly CancellationTokenSource _stop = new();
    private long _lastBeat = Environment.TickCount64;
    private int _logs;

    /// Starts watching.
    public UiWatchdog()
    {
        _worker = new Thread(Watch) { IsBackground = true, Name = "UI watchdog" };
        _worker.Start();
    }

    // Each look first checks whether the UI thread ran the heartbeat posted at the look before, then posts the next one: a stall is the
    // time since an unanswered post, so the heartbeat can be slow (it wakes the UI thread only every few seconds).
    private void Watch()
    {
        var stuckLogged = false;
        var posted = 0L;
        while (!_stop.Token.WaitHandle.WaitOne(Timing.UiHeartbeatEvery))
        {
            Guard.Run("UI watchdog", () =>
            {
                var now = Environment.TickCount64;
                var beat = Interlocked.Read(ref _lastBeat);
                // Still unrun: the stall so far; run late: how late (a stall that ended between two looks).
                var silent = posted == 0 ? 0 : beat < posted ? now - posted : beat - posted;
                if (silent < Timing.UiStuckAfter.TotalMilliseconds) stuckLogged = false;
                else if (!stuckLogged && _logs < Timing.UiStuckLogsPerRun)
                {
                    stuckLogged = true;
                    _logs++;
                    AppDataStore.AppendLog(LogFile, $"The UI thread has not answered for {silent} ms.");
                }
                if (beat < posted) return; // the post before is still waiting: no second one behind it
                posted = now;
                Dispatcher.UIThread.Post(() => Interlocked.Exchange(ref _lastBeat, Environment.TickCount64), DispatcherPriority.Send);
            });
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_worker.Join(Timing.UiHeartbeatEvery)) _stop.Dispose(); // a worker still writing a log keeps its token until the process ends
    }
}
