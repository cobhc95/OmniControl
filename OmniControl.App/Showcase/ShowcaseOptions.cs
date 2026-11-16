namespace OmniControl.App.Showcase;

/// <summary>
/// Developer builds only (this folder is not compiled in Release): the hidden screenshot export. What starts it, and the one place that
/// says where its files and its throw-away data folder go.
/// </summary>
internal static class ShowcaseOptions
{
    /// <summary>The command-line flag that runs the export: <c>OmniControl.exe --export-screenshots &lt;folder&gt;</c>.</summary>
    public const string Flag = "--export-screenshots";

    /// <summary>The environment variable <c>AppDataStore</c> reads in Debug builds; set only for an export run (see <see cref="UseThrowAwayData"/>).</summary>
    public const string DataFolderVariable = "OMNICONTROL_SHOWCASE_DATA";

    /// <summary>
    /// Added to <see cref="Flag"/>: show the owner's own setup. The saved settings are copied into the throw-away folder
    /// (<see cref="ShowcaseRealData"/>) and the run may look at the system without changing it (<c>StartupMode.ReadOnly</c>).
    /// </summary>
    public const string RealDataFlag = "--real-data";

    /// <summary>Set (to 1) for a <see cref="RealDataFlag"/> run; <c>StartupMode</c> reads it.</summary>
    public const string RealDataVariable = "OMNICONTROL_SHOWCASE_REAL";

    /// <summary>The folder after the flag, or null when the run is a normal one.</summary>
    public static string? ExportFolder(IReadOnlyList<string>? args)
    {
        if (args is null) return null;
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(args[i + 1]);
        return null;
    }

    /// <summary>
    /// For an export run, before anything reads a setting: points the app's data folder at a temporary one, so the run can change none of
    /// the owner's settings. It is empty (factory defaults, no personal data) unless <see cref="RealDataFlag"/> is given, when it holds a
    /// copy of the owner's saved settings. Returns that folder (null: a normal run).
    /// </summary>
    public static string? UseThrowAwayData(string[] args)
    {
        if (ExportFolder(args) is null) return null;
        var folder = Path.Combine(Path.GetTempPath(), $"OmniControl-showcase-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        if (args.Contains(RealDataFlag, StringComparer.OrdinalIgnoreCase))
        {
            ShowcaseRealData.CopyInto(folder); // before the variable is set: the store still names the owner's folder
            Environment.SetEnvironmentVariable(RealDataVariable, "1");
        }
        Environment.SetEnvironmentVariable(DataFolderVariable, folder);
        return folder;
    }

    /// <summary>The folder the Ctrl+Alt+Shift+F12 chord writes to: Pictures\OmniControl showcase\&lt;date&gt;.</summary>
    public static string ChordFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OmniControl showcase", DateTime.Now.ToString("yyyy-MM-dd"));
}
