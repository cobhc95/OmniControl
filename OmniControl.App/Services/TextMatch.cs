namespace OmniControl.App.Services;

/// <summary>The one search rule: a query matches when any of the fields contains it, ignoring case.</summary>
public static class TextMatch
{
    public static bool Any(string query, params string[] fields) =>
        fields.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase));
}
