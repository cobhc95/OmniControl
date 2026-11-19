namespace OmniControl.App.Features.Tray;

/// An id that may hold one <c>*</c>, standing for any text of at least one character (dots included: a display's id is Windows' device
/// name, <c>\\.\DISPLAY1</c>). Without a <c>*</c> it matches only itself. Pure, no UI.
public static class IdPattern
{
    public const char Any = '*';

    public static bool IsPattern(string pattern) => pattern.Contains(Any);

    public static bool Matches(string pattern, string id)
    {
        var at = pattern.IndexOf(Any);
        if (at < 0) return pattern == id;
        var (head, tail) = (pattern[..at], pattern[(at + 1)..]);
        return id.Length > head.Length + tail.Length && id.StartsWith(head, StringComparison.Ordinal) && id.EndsWith(tail, StringComparison.Ordinal);
    }
}
