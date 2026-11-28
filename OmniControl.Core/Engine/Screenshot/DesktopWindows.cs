using static OmniControl.Core.Native.ScreenshotNative;

namespace OmniControl.Core.Engine.Screenshot;

/// Reads the windows on screen for the area overlay, once, just before it opens: each visible top-level window front to back, with its
/// visible frame (DWM's extended frame bounds, without the invisible resize border), plus the monitors and their work areas (the taskbar's
/// edge). Hidden, minimised, cloaked (other virtual desktops, suspended apps), click-through and tool windows are left out, except the
/// taskbar; so are the desktop itself and windows parked off screen. Nothing runs after it returns.
public static class DesktopWindows
{
    /// At most this many windows are read (a real desktop has far fewer visible ones).
    private const int WindowLimit = 256;

    /// Edges shorter than this (physical pixels) are not worth snapping to.
    private const int ShortestEdge = 8;

    private static readonly string[] DesktopClasses = { "Progman", "WorkerW" };
    private static readonly string[] TaskbarClasses = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// The windows and edges now. Never throws for a window that closes meanwhile: it is simply left out.
    public static DesktopLayout Read()
    {
        var desktop = ScreenCapture.VirtualScreen();
        var found = new List<DesktopWindow>();
        var text = new char[256];
        bool Visit(IntPtr hwnd, IntPtr data)
        {
            if (Describe(hwnd, desktop, text) is { } window) found.Add(window);
            return found.Count < WindowLimit;
        }
        EnumWindows(Visit, IntPtr.Zero); // Windows lists top-level windows in z-order, front first
        var screens = ScreenCapture.WorkAreas().SelectMany(m => new[] { m.Bounds, m.Work });
        return SnapEdges.Build(found, screens, ShortestEdge);
    }

    private static DesktopWindow? Describe(IntPtr hwnd, ScreenRect desktop, char[] text)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return null;
        if (DwmGetWindowNumber(hwnd, DwmCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var type = Text(GetClassName(hwnd, text, text.Length), text);
        if (DesktopClasses.Contains(type)) return null;
        var taskbar = TaskbarClasses.Contains(type);
        var ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        if (!taskbar && (ex & (WsExToolWindow | WsExTransparent)) != 0) return null;
        var bounds = DwmGetWindowAttribute(hwnd, DwmExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>()) == 0
            ? frame
            : GetWindowRect(hwnd, out var rect) ? rect : default;
        var area = new ScreenRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top).Intersect(desktop);
        if (area.IsEmpty) return null;
        var name = taskbar ? "Taskbar" : Text(GetWindowText(hwnd, text, text.Length), text);
        return new DesktopWindow(area, name.Length > 0 ? name : "Window");
    }

    private static string Text(int length, char[] text) => length > 0 ? new string(text, 0, Math.Min(length, text.Length)) : string.Empty;
}
