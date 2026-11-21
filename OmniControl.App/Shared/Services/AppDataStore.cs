using System.Text;
using System.Text.Json;

namespace OmniControl.App.Shared.Services;

/// The one place that reads and writes OmniControl's json files under %APPDATA%\OmniControl, each by its entry in <see cref="DataStores"/>.
/// A missing or damaged file gives defaults (a damaged one is kept as <c>name.bad.json</c> and said once) and a failed save is logged:
/// settings are never worth stopping the app for.
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

    /// The saved value, or null when the file is missing, unreadable or damaged. A damaged file is copied to <c>name.bad.json</c> first (the
    /// next save replaces it), logged and listed once for <see cref="TakeDamaged"/>.
    public static T? Load<T>(DataStore store) where T : class
    {
        string? text = null;
        try
        {
            return (text = LoadText(store)) is null ? null : JsonSerializer.Deserialize<T>(text);
        }
        catch (JsonException ex)
        {
            KeepDamaged(store, text!, ex);
            return null;
        }
    }

    // File names found damaged this run, and those not yet said (under WriteGate).
    private static readonly HashSet<string> Damaged = new();
    private static readonly List<string> Unsaid = new();

    private static void KeepDamaged(DataStore store, string text, JsonException ex)
    {
        lock (WriteGate)
        {
            if (!Damaged.Add(store.FileName)) return;
            Unsaid.Add(store.FileName);
            var copy = BadCopyOf(store.FileName);
            try
            {
                File.WriteAllText(PathOf(copy), text);
                AppendLog(Guard.LogFile, $"{store.FileName} was damaged ({ex.Message}); its defaults are used and the file is kept as {copy}.");
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
                AppendLog(Guard.LogFile, $"{store.FileName} was damaged ({ex.Message}) and couldn't be kept as {copy}: {io.Message}");
            }
        }
    }

    /// The copy a damaged file is kept as: settings.json -> settings.bad.json.
    public static string BadCopyOf(string fileName) => Path.ChangeExtension(fileName, ".bad.json");

    /// The settings files found damaged since the last call (each said once), for one sentence in the status line.
    public static IReadOnlyList<string> TakeDamaged()
    {
        lock (WriteGate)
        {
            var taken = Unsaid.ToList();
            Unsaid.Clear();
            return taken;
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
            AppendLog(Guard.LogFile, $"Couldn't read {store.FileName}: {ex.Message}");
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
        try
        {
            Directory.CreateDirectory(Folder);
            WriteAtomically(PathOf(store.FileName), Encoding.UTF8.GetBytes(text));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or locked folder: keep running with the values in memory, and say so in the log.
            AppendLog(Guard.LogFile, $"Couldn't save {store.FileName}: {ex.Message}");
            return false;
        }
    }

    /// Writes a temp file then moves it over <paramref name="path"/>, so a crash or power cut mid-write leaves the old file whole, never a
    /// truncated one (the temp name is unique per call, so never shared). <paramref name="toDisk"/> waits until the bytes are on the disk
    /// before the move (a restore's journal and the files it names must outlive a power cut). Throws on an I/O failure, the temp file removed.
    public static void WriteAtomically(string path, byte[] bytes, bool toDisk = false)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                if (toDisk) file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: a store is read again, harmlessly, at the next start; a temp file is never read.
            AppendLog(Guard.LogFile, $"Couldn't delete {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    /// Appends a line to a diagnostics file, stamped now and written by the background log writer (<see cref="LogWriter"/>); never throws or waits.
    public static void AppendLog(string fileName, string line) =>
        LogWriter.Append(PathOf(fileName), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");

    /// Writes the queued log lines now (at exit, or before the process ends on an unhandled exception).
    public static void FlushLogs() => LogWriter.Flush();
}
