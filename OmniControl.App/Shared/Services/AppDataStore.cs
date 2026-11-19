using System.Text.Json;

namespace OmniControl.App.Shared.Services;

/// The one place that reads and writes OmniControl's json files under %APPDATA%\OmniControl, each by its entry in <see cref="DataStores"/>.
/// A missing or damaged file gives defaults and a failed save is ignored: settings are never worth stopping the app for.
/// A store changed often (a slider) saves with <see cref="SaveSoon{T}"/>: the text is taken at once and written by a worker
/// <see cref="Timing.SaveSoonDelay"/> after the last change of a burst; a read of that store, a direct save or delete of it, and
/// <see cref="FlushSaves"/> (exit, backup, import) write or drop the waiting text first, so nothing reads an older file or overwrites a newer one.
public static class AppDataStore
{
    /// Settable only by the test assembly (internal), so tests never write into the real app data.
    public static string Folder { get; internal set; } = AppDataLocation.Default;

    // The saves waiting (file name -> store and text), and the one gate every file write of a store passes, so writes keep their order.
    // Process-wide on purpose, like the files themselves (an allowed exception to "no static state", MAP.md).
    private static readonly object PendingGate = new(), WriteGate = new();
    private static readonly Dictionary<string, (DataStore Store, string Text)> Pending = new();
    private static readonly Timer PendingDue = new(_ => Guard.Run("Saving settings", FlushSaves));

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// Whether any settings file (settings, mixer, hotkeys...) was saved by an earlier run: false only on a new install.
    public static bool HasSavedFiles()
    {
        try { return Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.json").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } // unreadable folder: treated as new, nothing is lost by opening in Easy
    }

    /// The saved value, or null when the file is missing, unreadable or damaged.
    public static T? Load<T>(DataStore store) where T : class
    {
        try
        {
            return LoadText(store) is { } text ? JsonSerializer.Deserialize<T>(text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save<T>(DataStore store, T value) => SaveText(store, JsonSerializer.Serialize(value));

    /// Saves <paramref name="value"/> as it is now, written to the file a moment later off the calling thread (see the class). Any thread.
    public static void SaveSoon<T>(DataStore store, T value)
    {
        var text = JsonSerializer.Serialize(value);
        lock (PendingGate) Pending[store.FileName] = (store, text);
        PendingDue.Change(Timing.SaveSoonDelay, Timeout.InfiniteTimeSpan);
    }

    /// Writes every waiting save now, on this thread (exit, before a backup or an import reads the folder).
    public static void FlushSaves()
    {
        lock (WriteGate)
        {
            List<(DataStore Store, string Text)> due;
            lock (PendingGate)
            {
                due = Pending.Values.ToList();
                Pending.Clear();
            }
            foreach (var (store, text) in due) Write(store, text);
        }
    }

    /// The file's text exactly as saved, or null when it is missing or unreadable.
    public static string? LoadText(DataStore store)
    {
        try
        {
            lock (WriteGate)
            {
                if (TakePending(store) is { } waiting) Write(store, waiting);
                var path = PathOf(store.FileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// Saves the text atomically now (a waiting older save of the store is dropped); false (and a line in the log) when the folder could not be written.
    public static bool SaveText(DataStore store, string text)
    {
        lock (WriteGate)
        {
            TakePending(store);
            return Write(store, text);
        }
    }

    /// Removes a file (and a waiting save of it); a missing or locked file is not a problem.
    public static void Delete(DataStore store)
    {
        lock (WriteGate)
        {
            TakePending(store);
            TryDelete(PathOf(store.FileName));
        }
    }

    /// Takes the waiting save of <paramref name="store"/> out of the queue (call inside the write gate).
    private static string? TakePending(DataStore store)
    {
        lock (PendingGate) return Pending.Remove(store.FileName, out var waiting) ? waiting.Text : null;
    }

    /// Writes the file atomically (call inside the write gate).
    private static bool Write(DataStore store, string text)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Folder);
            // Temp file then move: a crash or power cut mid-write leaves the old file whole, never a truncated one. The temp file's name is
            // unique per call, so a temp file is never shared.
            var path = PathOf(store.FileName);
            temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or locked folder: keep running with the values in memory, and say so in the log.
            AppendLog(Guard.LogFile, $"Couldn't save {store.FileName}: {ex.Message}");
            if (temp is not null) TryDelete(temp);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: a store is read again, harmlessly, at the next start; a temp file is never read.
        }
    }

    /// Appends a line to a diagnostics file, stamped now and written by the background log writer (<see cref="LogWriter"/>); never throws or waits.
    public static void AppendLog(string fileName, string line) =>
        LogWriter.Append(PathOf(fileName), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");

    /// Writes the queued log lines now (at exit, or before the process ends on an unhandled exception).
    public static void FlushLogs() => LogWriter.Flush();
}
