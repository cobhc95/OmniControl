using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// Opens a web page (or an ms-settings: page) with the shell, with the user's normal rights. The one place that does it: only https: and
/// ms-settings: addresses (plain http: only where the caller says so: a router's page, a link the user picked in a screenshot), never a
/// file or another program's protocol.
public static class WebPage
{
    /// Returns an error to show, or null when the page was opened.
    public static string? Open(string url, bool allowHttp = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme is "https" or "ms-settings" || (allowHttp && uri.Scheme == "http")))
            return $"Couldn't open {url}: it is not a web or Settings page.";
        try
        {
            NormalRights.Open(uri.OriginalString);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Guard.Log("Opening a web page", ex);
            return $"Couldn't open {url}: {ex.Message}";
        }
    }
}
