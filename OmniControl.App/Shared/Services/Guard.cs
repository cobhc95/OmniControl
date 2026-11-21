namespace OmniControl.App.Shared.Services;

/// THE isolation boundary for entry points (an action run, a timer callback, a start-up or shut-down step): one feature failing is
/// logged to <see cref="LogFile"/> and never reaches the next one. Only the code that calls it decides what to tell the person.
public static class Guard
{
    public const string LogFile = "errors-log.txt";

    /// Runs <paramref name="step"/>; a failure is logged with <paramref name="what"/> and returned (null when it worked).
    public static Exception? Run(string what, Action step)
    {
        try
        {
            step();
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(what, ex);
            return ex;
        }
    }

    /// Runs <paramref name="step"/> to its end; a failure is logged with <paramref name="what"/> and never escapes (for a task nobody awaits).
    public static async Task RunAsync(string what, Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(what, ex);
        }
    }

    /// Writes a failure and its stack to the errors log.
    public static void Log(string what, Exception ex) => AppDataStore.AppendLog(LogFile, $"{what}: {ex}");

    /// THE words that send the person to a log: "<paramref name="sentence"/> Details are in <paramref name="logFile"/>." (the errors log when none is named).
    public static string SeeLog(string sentence, string logFile = LogFile) => $"{sentence} Details are in {logFile}.";

    /// Runs <paramref name="work"/> on a worker; a failure is logged with <paramref name="what"/> and never escapes (THE way to start work nobody awaits).
    public static void OffUi(string what, Func<Task> work) => _ = Task.Run(() => RunAsync(what, work));

    /// Runs <paramref name="work"/> on a worker; a failure is logged with <paramref name="what"/> and never escapes.
    public static void OffUi(string what, Action work) => _ = Task.Run(() => Run(what, work));

    /// For an <c>async void</c> event handler: runs <paramref name="step"/> to its end; a failure is logged and said in one sentence through <paramref name="report"/> (when given), never thrown into the UI loop.
    public static async void Fire(string what, Func<Task> step, Action<string>? report = null)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(what, ex);
            report?.Invoke(SeeLog($"{what} didn't work."));
        }
    }

    /// The full path of the errors log, for "Copy details".
    public static string LogPath => AppDataStore.PathOf(LogFile);
}
