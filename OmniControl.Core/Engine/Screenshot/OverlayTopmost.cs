using static OmniControl.Core.Native.ScreenshotNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// Keeps the capture overlay above other windows in its strong mode: a top-most tool window that puts itself back on top and takes the
/// foreground again when another program took it. Windows' UIPI still keeps windows of elevated programs (Task Manager run as administrator)
/// above a program that is not elevated; nothing here can change that.
/// </summary>
public static class OverlayTopmost
{
    /// <summary>Makes the window a top-most tool window (no taskbar button, not in Alt+Tab) and puts it on top.</summary>
    public static void MakeStrong(IntPtr window)
    {
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64() | WsExTopmost | WsExToolWindow;
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(style));
        PutOnTop(window);
    }

    /// <summary>Puts the window back at the top of the top-most windows without moving, sizing or activating it.</summary>
    public static void PutOnTop(IntPtr window) =>
        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);

    /// <summary>True when the foreground window belongs to this program (an overlay, its pop-ups or a dialog it opened).</summary>
    public static bool OwnsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out var pid) != 0 && pid == Environment.ProcessId;
    }

    /// <summary>
    /// Brings the window to the foreground even when another program has it: Windows only lets the program with the last input do that,
    /// so the call is made with this thread's input attached to the foreground window's thread for a moment (always let go again).
    /// It never hands the foreground right on to other programs (AllowSetForegroundWindow(ASFW_ANY) did, which let any program take the
    /// foreground from the one the user picked on the taskbar).
    /// </summary>
    public static void TakeForeground(IntPtr window)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, foregroundThread, false);
        }
    }
}
