namespace OmniControl.Core.Engine;

/// Where a gadget of a given size sits on the taskbar (physical pixels, screen), and the taskbar window it is put into.
public sealed record TaskbarPlace(IntPtr Taskbar, int X, int Y);

/// THE placement of a small window over the Windows taskbar (Windows 11 has no taskbar toolbars): left of the tray icons, centred in
/// the taskbar's height (the left end would overlap the taskbar's program icons). <see cref="Attach"/> makes it a child of the taskbar, so it is drawn as part of the taskbar
/// and moves with it into whatever layer Windows 11 lifts the taskbar to (an owned or topmost window is covered then). Explorer restarting
/// destroys the old taskbar and its children: find and attach again.
public static class TaskbarSpot
{
    private const double BaseDpi = 96;

    /// The place for a <paramref name="width"/> x <paramref name="height"/> window, <paramref name="gap"/> device-independent pixels left of the tray (negative: over the tray's own
    /// padding by that much), kept inside the taskbar; null when there is no taskbar (Explorer is not running).
    public static TaskbarPlace? Find(int width, int height, double gap)
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out var b)) return null;
        var tray = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        var right = tray != IntPtr.Zero && GetWindowRect(tray, out var t) && t.Right > t.Left ? t.Left : b.Right;
        var space = (int)Math.Round(gap * GetDpiForWindow(bar) / BaseDpi);
        var x = Math.Clamp(right - width - space, b.Left, Math.Max(b.Left, b.Right - width));
        return new TaskbarPlace(bar, x, b.Top + (b.Bottom - b.Top - height) / 2);
    }

    /// Makes <paramref name="window"/> a child of the taskbar (true); where Windows refuses that, the taskbar owns it instead (false: it is
    /// not hidden with the taskbar, see <see cref="FullScreenOn"/>). Either way it never takes focus and stays out of Alt+Tab.
    public static bool Attach(IntPtr window, IntPtr taskbar)
    {
        var style = GetWindowLongPtr(window, StyleIndex).ToInt64();
        SetWindowLongPtr(window, StyleIndex, new IntPtr((style & ~Popup) | Child));
        var child = SetParent(window, taskbar) != IntPtr.Zero;
        if (!child)
        {
            SetWindowLongPtr(window, StyleIndex, new IntPtr(style));
            SetWindowLongPtr(window, OwnerIndex, taskbar);
        }
        SetWindowLongPtr(window, ExStyleIndex, new IntPtr(GetWindowLongPtr(window, ExStyleIndex).ToInt64() | ToolWindow | NoActivate));
        return child;
    }

    /// True when a full-screen app (Windows' own answer, <see cref="Mouse.FullScreenWatcher.AppInFront"/>) is in front on the monitor of
    /// <paramref name="taskbar"/>: Windows hides the taskbar there, so an owned window over it must hide too.
    public static bool FullScreenOn(IntPtr taskbar) =>
        Mouse.FullScreenWatcher.AppInFront() && MonitorFromWindow(GetForegroundWindow(), DefaultToNearest) == MonitorFromWindow(taskbar, DefaultToNearest);

    /// Shows <paramref name="window"/> again without activating it, or hides it.
    public static void Show(IntPtr window, bool shown) => ShowWindow(window, shown ? ShowNoActivate : Hide);

    /// Moves <paramref name="window"/> to <paramref name="place"/> and to the top of its siblings (or of the topmost windows when owned).
    public static void Put(IntPtr window, TaskbarPlace place)
    {
        var at = new Point { X = place.X, Y = place.Y };
        ScreenToClient(GetAncestor(window, AncestorParent), ref at); // the desktop's client is the screen, so an owned window is unchanged
        SetWindowPos(window, IntPtr.Zero, at.X, at.Y, 0, 0, NoSize | NoActivateOnMove); // after = zero: the top
    }

    /// True when another child of the taskbar is drawn over <paramref name="window"/> (Explorer re-ordered its own parts).
    public static bool IsCovered(IntPtr window) =>
        (GetWindowLongPtr(window, StyleIndex).ToInt64() & Child) != 0 && GetWindow(window, PreviousSibling) != IntPtr.Zero;

    /// The DPI Windows draws <paramref name="window"/> at (the taskbar's: its screen's scale).
    public static int Dpi(IntPtr window) => (int)GetDpiForWindow(window);

    /// True when the taskbar is light (Windows' "system" colour mode), so text on it is dark.
    public static bool IsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    private const int OwnerIndex = -8, StyleIndex = -16, ExStyleIndex = -20;
    private const long Child = 0x40000000, Popup = 0x80000000, ToolWindow = 0x80, NoActivate = 0x08000000;
    private const uint NoSize = 0x0001, NoActivateOnMove = 0x0010, AncestorParent = 1, PreviousSibling = 3, DefaultToNearest = 2;
    private const int Hide = 0, ShowNoActivate = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
