using System.Runtime.InteropServices;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE "Copy details" action: puts a failure's details on the clipboard of the window that holds <c>from</c>.
public static class CopyDetails
{
    public static async Task ToClipboardAsync(Visual from, string? details)
    {
        if (string.IsNullOrEmpty(details) || TopLevel.GetTopLevel(from)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(details);
        }
        catch (Exception ex) when (ex is TimeoutException or ExternalException)
        {
            // Another program holds the clipboard: the details are still in the log, whose path the message or status line names.
        }
    }
}
