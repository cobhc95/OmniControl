using System.Text.Json;

namespace OmniControl.App.Services;

/// <summary>
/// The one place that reads and writes OmniControl's json files under %APPDATA%\OmniControl.
/// A missing or damaged file gives defaults and a failed save is ignored: settings are never worth stopping the app for.
/// </summary>
public static class AppDataStore
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl");

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Whether any settings file (settings, mixer, hotkeys...) was saved by an earlier run: false only on a new install.</summary>
    public static bool HasSavedFiles()
    {
        try { return Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.json").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } // unreadable folder: treated as new, nothing is lost by opening in Easy
    }

    /// <summary>The saved value, or null when the file is missing, unreadable or damaged.</summary>
    public static T? Load<T>(string fileName) where T : class
    {
        try
        {
            var path = PathOf(fileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathOf(fileName), JsonSerializer.Serialize(value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or locked folder: keep running with the values in memory.
        }
    }

    /// <summary>Removes a file; a missing or locked file is not a problem.</summary>
    public static void Delete(string fileName)
    {
        try { File.Delete(PathOf(fileName)); }
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
