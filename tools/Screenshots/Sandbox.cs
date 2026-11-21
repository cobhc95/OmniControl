namespace OmniControl.Screenshots;

/// Keeps the tool's files away from the user's: every settings file, log and saved list goes to a fresh temp folder (deleted at the end),
/// and the real data folder is listed before and after so a write that slipped through would be reported.
internal sealed class Sandbox : IDisposable
{
    private readonly string _folder;
    private readonly Dictionary<string, (long Size, DateTime Written)> _realBefore;

    private Sandbox(string folder)
    {
        _folder = folder;
        _realBefore = ListReal();
    }

    public static Sandbox Enter()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OmniControl-screenshots", Environment.ProcessId.ToString());
        Directory.CreateDirectory(folder);
        AppDataStore.Folder = folder;
        if (!string.Equals(AppDataStore.Folder, folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The data folder could not be moved to the temp folder; nothing was drawn.");
        return new Sandbox(folder);
    }

    /// Says whether any file of the real data folder changed while the tool ran (the running app itself may write there too).
    public void CheckRealDataUntouched()
    {
        AppDataStore.FlushSaves();
        AppDataStore.FlushLogs();
        var after = ListReal();
        var changed = after.Where(p => !_realBefore.TryGetValue(p.Key, out var was) || was != p.Value).Select(p => p.Key)
            .Concat(_realBefore.Keys.Where(k => !after.ContainsKey(k))).ToList();
        Console.WriteLine(changed.Count == 0
            ? $"Real data folder untouched ({_realBefore.Count} files compared)."
            : $"Real data folder: changed while the tool ran (the running app may also write): {string.Join(", ", changed)}");
    }

    private static Dictionary<string, (long, DateTime)> ListReal()
    {
        var real = AppDataLocation.Default;
        if (!Directory.Exists(real)) return new();
        return new DirectoryInfo(real).EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(real, f.FullName), f => (f.Length, f.LastWriteTimeUtc));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"Temp folder left: {_folder} ({ex.Message})"); }
    }
}
