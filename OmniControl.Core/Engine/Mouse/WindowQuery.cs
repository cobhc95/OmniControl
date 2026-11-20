namespace OmniControl.Core.Engine.Mouse;

/// Finds the window under a screen point, the active window, a window's class name, and which process owns a window.
public static class WindowQuery
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    private const uint GaRoot = 2, MonitorDefaultToNull = 0;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;

    /// The programs that draw Windows' own shell (taskbar, Start, pop-ups, File Explorer windows).
    private static readonly string[] ShellPrograms = { "explorer.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe" };

    /// The top-level window the user is working in (the one with keyboard focus), or zero.
    internal static IntPtr ActiveWindow()
    {
        var foreground = GetForegroundWindow();
        return foreground == IntPtr.Zero ? IntPtr.Zero : GetAncestor(foreground, GaRoot);
    }

    /// The keyboard layout of the window the user types in (layouts can differ per app); the calling thread's when no window is active.
    internal static IntPtr ActiveKeyboardLayout() => GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));

    /// The exact window (possibly a child control) under the point, or zero.
    internal static IntPtr WindowAt(int x, int y) => WindowFromPoint(new Point { X = x, Y = y });

    /// The top-level window under the point, or zero.
    internal static IntPtr RootWindowAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new Point { X = x, Y = y });
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GaRoot);
    }

    /// The window's class name ("Progman", "Shell_TrayWnd"...), or empty when it has none.
    internal static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    /// True when the window is full screen: no title bar and it covers its whole monitor (or more, a session spanning monitors). A maximised
    /// window keeps its title bar, so it never counts even where it reaches past the monitor's edges.
    internal static bool CoversItsMonitor(IntPtr window)
    {
        if ((GetWindowLongPtr(window, GwlStyle).ToInt64() & WsCaption) == WsCaption || !GetWindowRect(window, out var r)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNull), ref info)) return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// The class name of the top-level window under the point, or empty. Cheap enough for the mouse hook.
    public static string RootClassAt(int x, int y)
    {
        var root = RootWindowAt(x, y);
        return root == IntPtr.Zero ? string.Empty : ClassOf(root);
    }

    /// The id of the process that owns the active window, or 0 when there is none. Cheap enough for the mouse hook.
    public static int ActiveProcessId() => ProcessOf(ActiveWindow()) ?? 0;

    /// The id of the process that owns the window under the point, or 0 when there is none. Cheap enough for the mouse hook.
    public static int ProcessIdAt(int x, int y) => ProcessOf(WindowAt(x, y)) ?? 0;

    /// The id of the process that owns the window, or null when there is no such window.
    internal static int? ProcessOf(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetWindowThreadProcessId(hwnd, out var pid) != 0 ? (int)pid : null;

    /// True when the process is one of Windows' shell programs (explorer.exe and the shell's experience hosts). Reads the process's path: not for the mouse hook.
    public static bool IsShellProgram(int processId)
    {
        if (processId == 0 || CoreAudio.ProcessPath(processId) is not { } path) return false;
        var name = Path.GetFileName(path);
        return ShellPrograms.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }
}
