using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>The kinds of place the pointer can be that a gesture cares about.</summary>
public enum PointerAreaKind { Elsewhere, Taskbar, TopEdge, LeftEdge, RightEdge, Desktop, TitleBar }

/// <summary>Works out which <see cref="PointerAreaKind"/> a screen point is in.</summary>
public static class PointerArea
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr w, IntPtr l, uint flags, uint timeoutMs, out UIntPtr result);

    private const uint MonitorDefaultToNearest = 2, WmNcHitTest = 0x0084, SmtoAbortIfHung = 0x0002;
    private const int HtCaption = 2;
    internal static readonly string[] TaskbarClasses = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
    private static readonly string[] DesktopClasses = { "Progman", "WorkerW" };

    /// <summary>
    /// Which area the point is in. Order decides where areas overlap: taskbar, then screen edges, then desktop, then title bar.
    /// <see cref="AreaMap"/> draws the areas cut in this same order.
    /// </summary>
    /// <param name="edgeStrip">How many pixels deep the strip along a screen edge is.</param>
    public static PointerAreaKind At(int x, int y, int edgeStrip)
    {
        var root = WindowQuery.RootWindowAt(x, y);
        var className = root == IntPtr.Zero ? string.Empty : WindowQuery.ClassOf(root);

        if (IsTaskbar(className)) return PointerAreaKind.Taskbar;
        if (EdgeAt(x, y, edgeStrip) is { } edge) return edge;
        if (IsDesktopClass(className)) return PointerAreaKind.Desktop;
        return root != IntPtr.Zero && IsCaption(root, x, y) == true ? PointerAreaKind.TitleBar : PointerAreaKind.Elsewhere;
    }

    internal static bool IsTaskbar(string className) => TaskbarClasses.Contains(className);
    internal static bool IsDesktopClass(string className) => DesktopClasses.Contains(className);

    private static PointerAreaKind? EdgeAt(int x, int y, int strip)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNearest), ref info)) return null;
        var monitor = new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        foreach (var edge in Edges(monitor, strip))
            if (edge.Rect.Contains(x, y)) return edge.Kind;
        return null;
    }

    /// <summary>
    /// The edge strips of one monitor, <paramref name="strip"/> pixels deep: the top strip runs the full width (it owns the corners),
    /// the left and right strips run below it. The one definition of the edges, used by the hit test and by <see cref="AreaMap"/>.
    /// </summary>
    internal static ScreenArea[] Edges(ScreenRect monitor, int strip) => new ScreenArea[]
    {
        new(PointerAreaKind.TopEdge, monitor with { Bottom = monitor.Top + strip }),
        new(PointerAreaKind.LeftEdge, new ScreenRect(monitor.Left, monitor.Top + strip, monitor.Left + strip, monitor.Bottom)),
        new(PointerAreaKind.RightEdge, new ScreenRect(monitor.Right - strip, monitor.Top + strip, monitor.Right, monitor.Bottom)),
    };

    /// <summary>
    /// The one title-bar test: asks the window what is at the point. Null when the window did not answer in time; the hit test then
    /// counts it as "not a title bar" rather than freezing the mouse. <see cref="CaptionArea"/> maps a window's title bar with this same test.
    /// </summary>
    internal static bool? IsCaption(IntPtr window, int x, int y)
    {
        var position = (IntPtr)((y << 16) | (x & 0xFFFF));
        var sent = SendMessageTimeout(window, WmNcHitTest, UIntPtr.Zero, position, SmtoAbortIfHung, CoreTiming.HitTestTimeoutMs, out var result);
        return sent == IntPtr.Zero ? null : (int)result == HtCaption;
    }

    /// <summary>True when the point is over the desktop (wallpaper or icons). Cheap enough for the mouse hook.</summary>
    public static bool IsDesktop(int x, int y) => IsDesktopClass(WindowQuery.RootClassAt(x, y));
}
