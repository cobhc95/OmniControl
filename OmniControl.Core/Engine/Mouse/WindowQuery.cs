using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>Finds the window under a screen point, the active window, a window's class name, and which process owns a window.</summary>
public static class WindowQuery
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    private const uint GaRoot = 2;

    /// <summary>The top-level window the user is working in (the one with keyboard focus), or zero.</summary>
    internal static IntPtr ActiveWindow()
    {
        var foreground = GetForegroundWindow();
        return foreground == IntPtr.Zero ? IntPtr.Zero : GetAncestor(foreground, GaRoot);
    }

    /// <summary>The exact window (possibly a child control) under the point, or zero.</summary>
    internal static IntPtr WindowAt(int x, int y) => WindowFromPoint(new Point { X = x, Y = y });

    /// <summary>The top-level window under the point, or zero.</summary>
    internal static IntPtr RootWindowAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new Point { X = x, Y = y });
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GaRoot);
    }

    /// <summary>The window's class name ("Progman", "Shell_TrayWnd"...), or empty when it has none.</summary>
    internal static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    /// <summary>The class name of the top-level window under the point, or empty. Cheap enough for the mouse hook.</summary>
    public static string RootClassAt(int x, int y)
    {
        var root = RootWindowAt(x, y);
        return root == IntPtr.Zero ? string.Empty : ClassOf(root);
    }

    /// <summary>The id of the process that owns the active window, or 0 when there is none. Cheap enough for the mouse hook.</summary>
    public static int ActiveProcessId() => ProcessOf(ActiveWindow()) ?? 0;

    /// <summary>The id of the process that owns the window under the point, or 0 when there is none. Cheap enough for the mouse hook.</summary>
    public static int ProcessIdAt(int x, int y) => ProcessOf(WindowAt(x, y)) ?? 0;

    /// <summary>The id of the process that owns the window, or null when there is no such window.</summary>
    internal static int? ProcessOf(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetWindowThreadProcessId(hwnd, out var pid) != 0 ? (int)pid : null;
}
