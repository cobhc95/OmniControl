namespace OmniControl.App.Features.OtherPcs;

/// Where the work of Other PCs runs: its state lives on the UI thread, and the network and the programs it waits on never do.
internal static class PcWork
{
    /// Runs <paramref name="work"/> on the UI thread; a failure in it is logged and never reaches the caller (a network task).
    public static void Post(Action work) => Dispatcher.UIThread.Post(() => Guard.Run("Other PCs", work));

    /// Runs <paramref name="work"/> on a worker (reading Tailscale, netsh and the administrator prompt all wait on other programs).
    public static void OffUi(string what, Func<Task> work) => _ = Task.Run(() => Guard.RunAsync(what, work));

    /// A task that ends when <paramref name="stop"/> is cancelled (never faults), to race against a wait.
    public static Task Until(CancellationToken stop) => Task.Delay(Timeout.Infinite, stop).ContinueWith(_ => { }, TaskScheduler.Default);
}
