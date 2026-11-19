namespace OmniControl.App.Shared.Services;

/// Watches that the UI thread keeps answering: a worker posts a heartbeat every <see cref="Timing.UiHeartbeatEvery"/>, and when the UI
/// thread has not run one for <see cref="Timing.UiStuckAfter"/> the stall and its length are written once to <see cref="LogFile"/> (no
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

    private void Watch()
    {
        var stuckLogged = false;
        while (!_stop.Token.WaitHandle.WaitOne(Timing.UiHeartbeatEvery))
        {
            Guard.Run("UI watchdog", () =>
            {
                Dispatcher.UIThread.Post(() => Interlocked.Exchange(ref _lastBeat, Environment.TickCount64), DispatcherPriority.Send);
                var silent = Environment.TickCount64 - Interlocked.Read(ref _lastBeat);
                if (silent < Timing.UiStuckAfter.TotalMilliseconds) { stuckLogged = false; return; }
                if (stuckLogged || _logs >= Timing.UiStuckLogsPerRun) return;
                stuckLogged = true;
                _logs++;
                AppDataStore.AppendLog(LogFile, $"The UI thread has not answered for {silent} ms.");
            });
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_worker.Join(Timing.UiHeartbeatEvery)) _stop.Dispose(); // a worker still writing a log keeps its token until the process ends
    }
}
