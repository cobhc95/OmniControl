using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// Makes sure OmniControl really ends when the last step of its exit hangs in code it doesn't control (a plug-in's own teardown):
/// if the guarded step has not finished within the limit, the process is ended. Use it only for a step after which nothing
/// remains to be saved or put back.
internal sealed class ExitGuard : IDisposable
{
    private readonly CancellationTokenSource _done = new();

    private ExitGuard(TimeSpan limit) =>
        // A thread-pool timer: it fires even while the UI thread is stuck inside the plug-in, where nothing else would end the process.
        Task.Delay(limit, _done.Token).ContinueWith(_ =>
            {
                AppDataStore.FlushLogs(); // the queued log lines say what hung
                Process.GetCurrentProcess().Kill();
            },
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public static ExitGuard Start(TimeSpan limit) => new(limit);

    public void Dispose()
    {
        _done.Cancel();
        _done.Dispose();
    }
}
