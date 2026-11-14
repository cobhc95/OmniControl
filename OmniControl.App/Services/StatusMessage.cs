namespace OmniControl.App.Services;

/// <summary>
/// A footer message in two sizes in one string, so every page keeps reporting through the one <c>Action&lt;string&gt;</c>: the short
/// line the footer shows, then its full explanation for the tooltip. A message with no second part is both.
/// </summary>
public static class StatusMessage
{
    private const char Divider = '\n';

    /// <summary>A short line for the footer and the full text for its tooltip.</summary>
    public static string Compose(string brief, string full) => $"{brief}{Divider}{full}";

    /// <summary>The line the footer shows.</summary>
    public static string BriefOf(string message) => message.Split(Divider, 2)[0];

    /// <summary>The text of the footer's tooltip.</summary>
    public static string FullOf(string message) => message.Split(Divider, 2) is { Length: 2 } parts ? parts[1] : message;
}
