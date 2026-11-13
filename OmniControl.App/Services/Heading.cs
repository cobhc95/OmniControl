namespace OmniControl.App.Services;

/// <summary>The one way a category and its groups are written as a single line: "Displays - Hardware".</summary>
public static class Heading
{
    private const string Separator = " - ";

    /// <summary>The non-empty parts, in order, joined by the separator.</summary>
    public static string Join(params string?[] parts) => string.Join(Separator, parts.Where(p => !string.IsNullOrEmpty(p)));
}
