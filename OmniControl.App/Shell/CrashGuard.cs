namespace OmniControl.App.Shell;

/// The last-chance net: an exception no feature caught (a UI event, an async void handler, a forgotten task) is logged and shown as one
/// plain sentence with "Copy details", and the app keeps running. Entry points still guard themselves (<see cref="Guard"/>); this is for
/// what they missed.
public static class CrashGuard
{
    private static bool _showing;

    public static void Install()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Tell("UI", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Guard.Log("Unhandled", ex);
            AppDataStore.FlushLogs(); // the process may end right after this handler
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Guard.Log("Unobserved task", e.Exception);
        };
    }

    private static async void Tell(string where, Exception ex)
    {
        Guard.Log(where, ex);
        if (_showing) return; // one window at a time: a storm of the same failure is in the log, not on screen
        _showing = true;
        try
        {
            await new DialogService().AskAsync(new DialogMessage("Something went wrong",
                $"OmniControl hit a problem and kept running. Copy the details if you report it; the log is {Guard.LogPath}.", Details: ex.ToString()));
        }
        catch (InvalidOperationException logged)
        {
            Guard.Log("Error window", logged); // the window itself could not open: the log already has the original
        }
        finally
        {
            _showing = false;
        }
    }
}
