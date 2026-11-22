using System.Text.RegularExpressions;

namespace OmniControl.Tests;

/// UTF-8 text read back as ANSI leaves these letter pairs ("â€¦" for an ellipsis, "Â·" for a middle dot); none may be in the source.
public class MojibakeTests
{
    private static readonly Regex Damaged = new("â€|Â[ -¿]|Ã[\u0080-¿]", RegexOptions.Compiled);

    [Fact]
    public void NoSourceFileHoldsUtf8ReadAsAnsi()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "OmniControl.App"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var hits = new[] { "OmniControl.App", "OmniControl.Core" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root!, project), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)).Where(x => Damaged.IsMatch(x.line)))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(hits);
    }
}
