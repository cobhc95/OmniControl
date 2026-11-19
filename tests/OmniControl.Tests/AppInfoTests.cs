using System.Text.RegularExpressions;

namespace OmniControl.Tests;

/// The version has one home (the project file); nothing else may spell it out.
public class AppInfoTests
{
    [Fact]
    public void VersionIsReadFromTheProject()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain('+', AppInfo.Version);
        Assert.Equal("Version: " + AppInfo.Version, AppInfo.VersionText);
    }

    [Fact]
    public void NoSourceFileSpellsOutTheVersionInAString()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "OmniControl.App"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var app = Path.Combine(root!, "OmniControl.App");
        var version = Regex.Escape(AppInfo.Version);
        // A quoted text holding the version as a whole number ("Version: 0.1", "OmniControl 0.1"), not 0.10 or 10.1.
        var literal = new Regex("\"[^\"]*(?<![0-9.])" + version + "(?![0-9.])[^\"]*\"");
        var hits = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i))
                .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///") && !x.line.TrimStart().StartsWith("<!--")
                            && literal.IsMatch(x.line) && Regex.IsMatch(x.line, "(?i)version|omnicontrol [0-9]")))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(hits);
    }
}
