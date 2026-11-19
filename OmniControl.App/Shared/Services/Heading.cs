namespace OmniControl.App.Shared.Services;

/// The one way a category and its groups are written as a single line: "Displays - Hardware".
public static class Heading
{
    private const string Separator = " - ";

    /// The non-empty parts, in order, joined by the separator.
    public static string Join(params string?[] parts) => string.Join(Separator, parts.Where(p => !string.IsNullOrEmpty(p)));
}
