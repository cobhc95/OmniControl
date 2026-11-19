namespace OmniControl.Core.Engine.Mouse;

/// The kinds of place the pointer can be that a gesture cares about.
public enum PointerAreaKind { Elsewhere, Taskbar, TopEdge, LeftEdge, RightEdge, Desktop, TitleBar }

/// Works out which <see cref="PointerAreaKind"/> a screen point is in.
public static class PointerArea
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr w, IntPtr l, uint flags, uint timeoutMs, out UIntPtr result);

    private const uint MonitorDefaultToNearest = 2, WmNcHitTest = 0x0084, SmtoAbortIfHung = 0x0002;
    private const int HtCaption = 2, GwlStyle = -16, WsCaption = 0x00C00000;
    internal static readonly string[] TaskbarClasses = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
    private static readonly string[] DesktopClasses = { "Progman", "WorkerW" };

    /// Which area the point is in. Order decides where areas overlap: taskbar, then screen edges, then desktop, then title bar.
    /// <see cref="AreaMap"/> draws the areas cut in this same order.
    /// <param name="edgeStrip">How many pixels deep the strip along a screen edge is.</param>
    /// <param name="titleBar">
    /// False when the caller has no use for <see cref="PointerAreaKind.TitleBar"/>: the window under the pointer is then never asked (the title-bar
    /// test sends it a message and waits up to <see cref="CoreTiming.HitTestTimeoutMs"/>, which the mouse hook should only do when it must).
    /// </param>
    public static PointerAreaKind At(int x, int y, int edgeStrip, bool titleBar = true)
    {
        var root = WindowQuery.RootWindowAt(x, y);
        var className = root == IntPtr.Zero ? string.Empty : WindowQuery.ClassOf(root);

        if (IsTaskbar(className)) return PointerAreaKind.Taskbar;
        if (MonitorAt(x, y) is { } monitor)
        {
            if (IsFullScreen(root, className, monitor) && InTaskbarStrip(x, y, monitor)) return PointerAreaKind.Taskbar;
            if (EdgeAt(x, y, monitor, edgeStrip) is { } edge) return edge;
        }
        if (IsDesktopClass(className)) return PointerAreaKind.Desktop;
        return titleBar && root != IntPtr.Zero && IsCaption(root, x, y) == true ? PointerAreaKind.TitleBar : PointerAreaKind.Elsewhere;
    }

    internal static bool IsTaskbar(string className) => TaskbarClasses.Contains(className);
    internal static bool IsDesktopClass(string className) => DesktopClasses.Contains(className);

    private static ScreenRect? MonitorAt(int x, int y)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNearest), ref info)) return null;
        return new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    private static PointerAreaKind? EdgeAt(int x, int y, ScreenRect monitor, int strip)
    {
        foreach (var edge in Edges(monitor, strip))
            if (edge.Rect.Contains(x, y)) return edge.Kind;
        return null;
    }

    /// The edge strips of one monitor, <paramref name="strip"/> pixels deep: the top strip runs the full width (it owns the corners),
    /// the left and right strips run below it. The one definition of the edges, used by the hit test and by <see cref="AreaMap"/>.
    internal static ScreenArea[] Edges(ScreenRect monitor, int strip) => new ScreenArea[]
    {
        new(PointerAreaKind.TopEdge, monitor with { Bottom = monitor.Top + strip }),
        new(PointerAreaKind.LeftEdge, new ScreenRect(monitor.Left, monitor.Top + strip, monitor.Left + strip, monitor.Bottom)),
        new(PointerAreaKind.RightEdge, new ScreenRect(monitor.Right - strip, monitor.Top + strip, monitor.Right, monitor.Bottom)),
    };

    /// Every taskbar window with its rectangle (an auto-hidden one's is mostly off its screen). Also used by <see cref="AreaMap"/>.
    internal static IEnumerable<(IntPtr Window, ScreenRect Rect)> Taskbars()
    {
        foreach (var className in TaskbarClasses)
        {
            var window = IntPtr.Zero;
            while ((window = FindWindowEx(IntPtr.Zero, window, className, null)) != IntPtr.Zero)
                if (GetWindowRect(window, out var r) && new ScreenRect(r.Left, r.Top, r.Right, r.Bottom) is { IsEmpty: false } rect) yield return (window, rect);
        }
    }

    /// A full-screen app (video, game) covers its whole monitor without a title bar; Windows then puts it over the taskbar, so the window
    /// under the pointer is never the taskbar. A maximized window keeps its caption style, so it never counts, even with an auto-hidden taskbar.
    private static bool IsFullScreen(IntPtr root, string className, ScreenRect monitor) =>
        root != IntPtr.Zero && !IsDesktopClass(className) && GetWindowRect(root, out var r)
        && r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom
        && (GetWindowLongPtr(root, GwlStyle).ToInt64() & WsCaption) != WsCaption;

    /// True when the point is in the strip of <paramref name="monitor"/> where its taskbar sits when shown (as deep as the taskbar), so
    /// the taskbar gesture works over a full-screen app as it does over the taskbar itself.
    private static bool InTaskbarStrip(int x, int y, ScreenRect monitor) =>
        Taskbars().Any(t => DockedStrip(monitor, t.Rect) is { } strip && strip.Contains(x, y));

    /// The strip along the side of <paramref name="monitor"/> a taskbar is docked to, or null when the taskbar is not on it.
    private static ScreenRect? DockedStrip(ScreenRect monitor, ScreenRect taskbar)
    {
        if (!taskbar.Intersects(monitor)) return null;
        if (taskbar.Width >= taskbar.Height)
        {
            if (taskbar.Bottom >= monitor.Bottom) return monitor with { Top = monitor.Bottom - taskbar.Height };
            if (taskbar.Top <= monitor.Top) return monitor with { Bottom = monitor.Top + taskbar.Height };
            return null;
        }
        if (taskbar.Right >= monitor.Right) return monitor with { Left = monitor.Right - taskbar.Width };
        if (taskbar.Left <= monitor.Left) return monitor with { Right = monitor.Left + taskbar.Width };
        return null;
    }

    /// The one title-bar test: asks the window what is at the point. Null when the window did not answer in time; the hit test then
    /// counts it as "not a title bar" rather than freezing the mouse. <see cref="CaptionArea"/> maps a window's title bar with this same test.
    internal static bool? IsCaption(IntPtr window, int x, int y)
    {
        var position = (IntPtr)((y << 16) | (x & 0xFFFF));
        var sent = SendMessageTimeout(window, WmNcHitTest, UIntPtr.Zero, position, SmtoAbortIfHung, CoreTiming.HitTestTimeoutMs, out var result);
        return sent == IntPtr.Zero ? null : (int)result == HtCaption;
    }

    /// True when the point is over the desktop (wallpaper or icons). Cheap enough for the mouse hook.
    public static bool IsDesktop(int x, int y) => IsDesktopClass(WindowQuery.RootClassAt(x, y));
}
