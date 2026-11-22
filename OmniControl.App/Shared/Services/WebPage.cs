using System.ComponentModel;

namespace OmniControl.App.Shared.Services;

/// Opens a web page (or an ms-settings: page) with the shell, with the user's normal rights. The one place that does it: only https: and
/// ms-settings: addresses (plain http: only where the caller says so: a router's page, a link the user picked in a screenshot), never a
/// file or another program's protocol.
public static class WebPage
{
    /// Returns an error to show when the address is not a page that may be opened, otherwise null. The opening itself runs off the calling
    /// (UI) thread; if Windows then refuses, the log gets the details and <paramref name="report"/> (thread-safe) one sentence.
    public static string? Open(string url, Action<string> report, bool allowHttp = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme is "https" or "ms-settings" || (allowHttp && uri.Scheme == "http")))
            return $"Couldn't open {url}: it is not a web or Settings page.";
        var address = uri.OriginalString;
        Guard.OffUi("Opening a web page", () =>
        {
            try
            {
                NormalRights.Open(address);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Guard.Log("Opening a web page", ex);
                report($"Couldn't open {url}: {ex.Message}");
            }
        });
        return null;
    }
}
