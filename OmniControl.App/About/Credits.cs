using Avalonia.Platform;

namespace OmniControl.App.About;

/// <summary>One project OmniControl uses or offers, as listed in NOTICE.txt.</summary>
public sealed record CreditEntry(string Name, string Version, string Licence, string Copyright, string Website, string UsedFor);

/// <summary>
/// The credits: read from NOTICE.txt (embedded in the app), which is the one list of third-party projects. Each entry starts
/// with a "Name:" line and ends at a blank line; the text before the first entry is OmniControl's own notice.
/// </summary>
public static class Credits
{
    private static readonly Uri NoticeUri = new("avares://OmniControl/Assets/NOTICE.txt");
    private const string NameField = "Name";

    /// <summary>The whole NOTICE.txt text.</summary>
    public static string NoticeText()
    {
        using var stream = AssetLoader.Open(NoticeUri);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The paragraphs before the first entry (OmniControl's licence and the trademark line).</summary>
    public static string Preamble(string notice)
    {
        var first = notice.IndexOf(NameField + ":", StringComparison.Ordinal);
        return (first < 0 ? notice : notice[..first]).Trim();
    }

    public static IReadOnlyList<CreditEntry> Entries(string notice)
    {
        var result = new List<CreditEntry>();
        foreach (var block in notice.Replace("\r\n", "\n").Split("\n\n"))
        {
            var fields = block.Split('\n')
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0].Trim())
                .ToDictionary(g => g.Key, g => g.First()[1].Trim());
            if (!fields.TryGetValue(NameField, out var name)) continue;
            string Field(string key) => fields.GetValueOrDefault(key, string.Empty);
            result.Add(new CreditEntry(name, Field("Version"), Field("Licence"), Field("Copyright"), Field("Website"), Field("Used for")));
        }
        return result;
    }
}
