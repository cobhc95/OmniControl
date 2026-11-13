using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.App.Services;

/// <summary>Opens a web page (or an ms-settings: page) with the shell. The one place that does it.</summary>
public static class WebPage
{
    /// <summary>Returns an error to show, or null when the page was opened.</summary>
    public static string? Open(string url)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Couldn't open {url}: {ex.Message}";
        }
    }
}
