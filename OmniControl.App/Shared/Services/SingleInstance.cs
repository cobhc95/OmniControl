namespace OmniControl.App.Shared.Services;

/// One OmniControl per Windows session (two copies were seen running at once: two mouse hooks, two sets of pop-ups and saves fighting over
/// the same files). The running copy holds a named mutex and listens on two named events; a later start sets the open event, so the running
/// copy opens its window, and ends at once; <c>OmniControl.exe --quit</c> (Setup and uninstall) sets the quit event, so the running copy
/// quits through its normal exit (every shut-down step runs). A restart (<c>--after</c>) instead waits for the copy before it to let go.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\OmniControl.Running";
    private const string OpenEventName = @"Local\OmniControl.Open", QuitEventName = @"Local\OmniControl.Quit";
    private const string QuitArg = "--quit";

    /// <c>--quit</c>'s exit codes: asked, or nothing runs; a running copy that cannot be asked (an older version);
    /// a copy run as administrator, which only an elevated <c>--quit</c> may ask (Windows keeps a normal process from its events).
    private const int Asked = 0, CannotAsk = 1, NeedsAdministrator = 2;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _open, _quit;
    private RegisteredWaitHandle? _listening, _quitting;

    private SingleInstance(Mutex mutex, EventWaitHandle? open, EventWaitHandle? quit) => (_mutex, _open, _quit) = (mutex, open, quit);

    public static bool IsQuit(string[] args) => args.Contains(QuitArg, StringComparer.OrdinalIgnoreCase);

    /// <c>--quit</c>: asks the running copy to quit; the exit code says how it went.
    public static int AskToQuit()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
            {
                using (quit) quit.Set();
                return Asked;
            }
            if (!Mutex.TryOpenExisting(MutexName, out var running)) return Asked; // nothing runs
            running.Dispose();
            return CannotAsk;
        }
        catch (UnauthorizedAccessException)
        {
            return NeedsAdministrator;
        }
    }

    /// When another copy runs: asks it to open its window (unless <paramref name="askToOpen"/> is false: a start in the background, the
    /// start-up entry, only ends) and returns true (this start then ends). A copy run as administrator cannot be asked by one that is not;
    /// it then simply keeps running and the line in the errors log says so.
    public static bool HandOff(bool askToOpen)
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var running)) return false;
            running.Dispose();
            if (!askToOpen) return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but belongs to a copy run as administrator: it is running.
            if (!askToOpen) return true;
        }
        try
        {
            if (EventWaitHandle.TryOpenExisting(OpenEventName, out var open))
            {
                using (open) open.Set();
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // A copy run as administrator: it cannot be asked by this one.
        }
        AppDataStore.AppendLog(Guard.LogFile, "Another OmniControl is already running (as administrator?) and could not be asked to open its window; this start ended.");
        return true;
    }

    /// Becomes the one running copy, waiting up to <paramref name="wait"/> for a copy that is closing (a restart). Null when another copy
    /// holds on: this start must end (it has asked that copy to open its window, when <paramref name="askToOpen"/>).
    public static SingleInstance? Claim(TimeSpan wait, bool askToOpen)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            HandOff(askToOpen); // held by a copy run as administrator
            return null;
        }
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the copy before ended without letting go (it was stopped): the mutex is now this copy's
        }
        if (!owned)
        {
            mutex.Dispose();
            HandOff(askToOpen);
            return null;
        }
        return new SingleInstance(mutex, Event(OpenEventName, "a later start can't ask this copy to open its window"), Event(QuitEventName, "Setup can't ask this copy to quit"));
    }

    private static EventWaitHandle? Event(string name, string unusable)
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            Guard.Log($"Single instance: {unusable}", ex);
            return null;
        }
    }

    /// From now on a later start runs <paramref name="open"/> instead of starting a second copy, and <c>--quit</c> runs <paramref name="quit"/>
    /// (both on a pool thread).
    public void Listen(Action open, Action quit)
    {
        if (_listening is not null) return;
        _listening = Wait(_open, "A later start asking to open the window", open);
        _quitting = Wait(_quit, "Setup asking OmniControl to quit", quit);
    }

    private static RegisteredWaitHandle? Wait(EventWaitHandle? signal, string what, Action run) => signal is null ? null
        : ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Guard.Run(what, run), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _listening?.Unregister(null);
        _quitting?.Unregister(null);
        _open?.Dispose();
        _quit?.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a thread that does not own it (an exit on another thread): Windows lets go when the process ends.
        }
        _mutex.Dispose();
    }
}
