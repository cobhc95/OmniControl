using System.Text.Json;

namespace OmniControl.App.Services.Updates;

/// <summary>Reads GitHub's list of releases (pure): the newest published one, pre-releases included.</summary>
public static class UpdateFeed
{
    /// <summary>The release with the highest version in <paramref name="json"/> (drafts and tags that are not versions are skipped); null when there is none. Throws <see cref="JsonException"/> when the text is not GitHub's list.</summary>
    public static ReleaseInfo? Newest(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("The release list is not an array.");
        ReleaseInfo? best = null;
        ReleaseVersion bestVersion = default;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Flag(item, "draft")) continue;
            var tag = Text(item, "tag_name");
            if (!ReleaseVersion.TryParse(tag, out var version) || (best is not null && !version.IsNewerThan(bestVersion))) continue;
            var url = Text(item, "html_url");
            best = new ReleaseInfo
            {
                Version = tag.Trim().TrimStart('v', 'V'),
                // Only a link on GitHub is trusted; anything else opens the releases page instead.
                Url = url.StartsWith("https://github.com/", StringComparison.Ordinal) ? url : AppInfo.ReleasesPageUrl,
                Published = item.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var when) ? when : null,
                Prerelease = Flag(item, "prerelease"),
            };
            bestVersion = version;
        }
        return best;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool Flag(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
