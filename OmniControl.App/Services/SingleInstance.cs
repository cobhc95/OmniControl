namespace OmniControl.App.Services;

/// <summary>
/// One OmniControl per Windows session (two copies were seen running at once: two mouse hooks, two sets of pop-ups and saves fighting over
/// the same files). The running copy holds a named mutex and listens on a named event; a later start sets that event, so the running copy
/// opens its window, and ends at once. A restart (<c>--after</c>) instead waits for the copy before it to let go.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\OmniControl.Running";
    private const string OpenEventName = @"Local\OmniControl.Open";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _open;
    private RegisteredWaitHandle? _listening;

    private SingleInstance(Mutex mutex, EventWaitHandle? open) => (_mutex, _open) = (mutex, open);

    /// <summary>
    /// When another copy runs: asks it to open its window (unless <paramref name="askToOpen"/> is false: a start in the background, the
    /// start-up entry, only ends) and returns true (this start then ends). A copy run as administrator cannot be asked by one that is not;
    /// it then simply keeps running and the line in the errors log says so.
    /// </summary>
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

    /// <summary>
    /// Becomes the one running copy, waiting up to <paramref name="wait"/> for a copy that is closing (a restart). Null when another copy
    /// holds on: this start must end (it has asked that copy to open its window, when <paramref name="askToOpen"/>).
    /// </summary>
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
        EventWaitHandle? open = null;
        try
        {
            open = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            Guard.Log("Single instance: a later start can't ask this copy to open its window", ex);
        }
        return new SingleInstance(mutex, open);
    }

    /// <summary>From now on a later start runs <paramref name="open"/> (on a pool thread) instead of starting a second copy.</summary>
    public void Listen(Action open)
    {
        if (_open is null || _listening is not null) return;
        _listening = ThreadPool.RegisterWaitForSingleObject(_open, (_, _) => Guard.Run("A later start asking to open the window", open), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _listening?.Unregister(null);
        _open?.Dispose();
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
