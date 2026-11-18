using System.Text.Json;

namespace OmniControl.App.Services;

/// <summary>
/// The one place that reads and writes OmniControl's json files under %APPDATA%\OmniControl, each by its entry in <see cref="DataStores"/>.
/// A missing or damaged file gives defaults and a failed save is ignored: settings are never worth stopping the app for.
/// </summary>
public static class AppDataStore
{
    /// <summary>Settable only by the test assembly (internal), so tests never write into the real app data.</summary>
    public static string Folder { get; internal set; } =
#if DEBUG
        Environment.GetEnvironmentVariable(Showcase.ShowcaseOptions.DataFolderVariable) ??
#endif
        AppDataLocation.Default;

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Whether any settings file (settings, mixer, hotkeys...) was saved by an earlier run: false only on a new install.</summary>
    public static bool HasSavedFiles()
    {
        try { return Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.json").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } // unreadable folder: treated as new, nothing is lost by opening in Easy
    }

    /// <summary>The saved value, or null when the file is missing, unreadable or damaged.</summary>
    public static T? Load<T>(DataStore store) where T : class
    {
        try
        {
            var path = PathOf(store.FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save<T>(DataStore store, T value) => SaveText(store, JsonSerializer.Serialize(value));

    /// <summary>The file's text exactly as saved, or null when it is missing or unreadable.</summary>
    public static string? LoadText(DataStore store)
    {
        try
        {
            var path = PathOf(store.FileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves the text atomically; false (and a line in the log) when the folder could not be written.</summary>
    public static bool SaveText(DataStore store, string text)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Folder);
            // Temp file then move: a crash or power cut mid-write leaves the old file whole, never a truncated one. The temp file's name is
            // unique per call, so two saves of one store at once never write the same temp file (the later move wins).
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

    /// <summary>Removes a file; a missing or locked file is not a problem.</summary>
    public static void Delete(DataStore store) => TryDelete(PathOf(store.FileName));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: a store is read again, harmlessly, at the next start; a temp file is never read.
        }
    }

    /// <summary>Appends a line to a diagnostics file; never throws.</summary>
    public static void AppendLog(string fileName, string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(PathOf(fileName), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are optional.
        }
    }
}
