namespace OmniControl.App.Shared.Services;

/// A footer message in two sizes in one string, so every page keeps reporting through the one <c>Action&lt;string&gt;</c>: the short
/// line the footer shows, then its full explanation for the tooltip. A message with no second part is both.
public static class StatusMessage
{
    private const char Divider = '\n';

    /// A short line for the footer and the full text for its tooltip.
    public static string Compose(string brief, string full) => $"{brief}{Divider}{full}";

    /// The line the footer shows.
    public static string BriefOf(string message) => message.Split(Divider, 2)[0];

    /// The text of the footer's tooltip.
    public static string FullOf(string message) => message.Split(Divider, 2) is { Length: 2 } parts ? parts[1] : message;
}
