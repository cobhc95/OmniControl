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

    /// The full path of the errors log, for "Copy details".
    public static string LogPath => AppDataStore.PathOf(LogFile);
}
