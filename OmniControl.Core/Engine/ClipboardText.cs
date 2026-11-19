namespace OmniControl.Core.Engine;

/// Reads plain text from the Windows clipboard.
public static class ClipboardText
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);

    private const uint UnicodeText = 13;

    /// The text now on the clipboard, or null when it holds none or could not be opened.
    public static string? Read()
    {
        for (var i = 0; i < CoreTiming.ClipboardAttempts; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    var data = GetClipboardData(UnicodeText);
                    if (data == IntPtr.Zero) return null;
                    var locked = GlobalLock(data);
                    if (locked == IntPtr.Zero) return null;
                    try { return Marshal.PtrToStringUni(locked); }
                    finally { GlobalUnlock(data); }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(CoreTiming.ClipboardWaitMs);
        }
        return null;
    }
}
