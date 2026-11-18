using System.Text;
using Avalonia.Threading;
using Microsoft.Diagnostics.Runtime;

namespace OmniControl.App.Services;

/// <summary>
/// Watches that the UI thread keeps answering: a worker posts a heartbeat every <see cref="Timing.UiHeartbeatEvery"/>, and when the UI
/// thread has not run one for <see cref="Timing.UiStuckAfter"/> the managed stack of every thread is written once to <see cref="LogFile"/>
/// (read from a snapshot of this process, so nothing is stopped for long), naming the UI thread. One log per stall, at most
/// <see cref="Timing.UiStuckLogsPerRun"/> per run; it never acts on the stall, it only tells where it is.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    public const string LogFile = "hang-log.txt";

    private readonly Thread _worker;
    private readonly int _uiThreadId;
    private readonly CancellationTokenSource _stop = new();
    private long _lastBeat = Environment.TickCount64;
    private int _logs;

    /// <summary>Starts watching; call on the UI thread.</summary>
    public UiWatchdog()
    {
        _uiThreadId = Environment.CurrentManagedThreadId;
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
                AppDataStore.AppendLog(LogFile, $"The UI thread has not answered for {silent} ms. Every thread's stack:{Environment.NewLine}{Stacks()}");
            });
        }
    }

    /// <summary>Each managed thread's stack, from a snapshot of this process (the process pauses only while the snapshot is taken).</summary>
    private string Stacks()
    {
        var text = new StringBuilder();
        try
        {
            using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
            using var runtime = target.ClrVersions[0].CreateRuntime();
            foreach (var thread in runtime.Threads.Where(t => t.IsAlive))
            {
                var ui = thread.ManagedThreadId == _uiThreadId ? " (UI thread)" : string.Empty;
                text.AppendLine($"-- thread {thread.ManagedThreadId}, OS id {thread.OSThreadId}{ui}");
                foreach (var frame in thread.EnumerateStackTrace().Take(Timing.UiStuckFramesPerThread))
                    text.AppendLine($"   {frame}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ClrDiagnosticsException or IndexOutOfRangeException)
        {
            // The snapshot can be refused (no rights, an unusual runtime): the stall is still logged, without stacks.
            text.AppendLine($"(stacks could not be read: {ex.Message})");
        }
        return text.ToString();
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_worker.Join(Timing.UiHeartbeatEvery)) _stop.Dispose(); // a worker still writing a log keeps its token until the process ends
    }
}
