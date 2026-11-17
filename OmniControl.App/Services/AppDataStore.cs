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

    public static void Save<T>(DataStore store, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Temp file then move: a crash or power cut mid-write leaves the old file whole, never a truncated one.
            var path = PathOf(store.FileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or locked folder: keep running with the values in memory, and say so in the log.
            AppendLog(Guard.LogFile, $"Couldn't save {store.FileName}: {ex.Message}");
        }
    }

    /// <summary>Removes a file; a missing or locked file is not a problem.</summary>
    public static void Delete(DataStore store)
    {
        try { File.Delete(PathOf(store.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: it is read again, harmlessly, at the next start.
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
