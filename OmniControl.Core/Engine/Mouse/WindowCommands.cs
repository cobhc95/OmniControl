namespace OmniControl.Core.Engine.Mouse;

/// Acts on the window under the mouse pointer: minimise, maximise, close, keep on top.
public static class WindowCommands
{
    // Async forms only: the window belongs to another program, and a synchronous call waits for that program's UI thread (a hung app would
    // stall the mouse queue, and every release behind it).
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    private const long ExLayered = 0x80000;
    private const uint LwaAlpha = 0x2;
    private const int MaxAlpha = 255, MinAlpha = 40; // below this a window is too faint to find again
    private const int SwMaximize = 3, SwMinimize = 6, SwRestore = 9, GwlExStyle = -20;
    private const uint WmClose = 0x0010, SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpAsyncWindowPos = 0x4000, ExTopmost = 0x8;
    private static readonly IntPtr HwndTopmost = new(-1), HwndNoTopmost = new(-2);

    // Never act on the desktop or taskbar: closing or minimising those is never what a button mapping means.
    private static readonly string[] ProtectedClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public static void Minimize(int x, int y, bool activeWindow = false) => With(x, y, activeWindow, h => ShowWindowAsync(h, SwMinimize));

    public static void ToggleMaximize(int x, int y, bool activeWindow = false) => With(x, y, activeWindow, h => ShowWindowAsync(h, IsZoomed(h) ? SwRestore : SwMaximize));

    /// Scrolls whatever is under the pointer by one line, or one page (vertical or sideways), with the window's own scroll messages, for
    /// windows that do not react to the wheel. <paramref name="forward"/> is down (or right).
    public static void ScrollWindow(int x, int y, bool vertical, bool forward, bool byPage = false)
    {
        var target = WindowQuery.WindowAt(x, y);
        if (target == IntPtr.Zero) return;
        const uint WmVScroll = 0x0115, WmHScroll = 0x0114;
        const int LineBackward = 0, LineForward = 1, PageBackward = 2, PageForward = 3;
        var request = byPage ? (forward ? PageForward : PageBackward) : (forward ? LineForward : LineBackward);
        PostMessage(target, vertical ? WmVScroll : WmHScroll, (UIntPtr)request, IntPtr.Zero);
    }

    /// Delivers wheel notches to the window under a screen point by posting the wheel message to it, like a real wheel would, but
    /// without going through the system input queue: input sent with SendInput from inside the program that owns the mouse hook
    /// stopped answering (the call never returned), so the scroll never happened. Positive is up (or right). False when no window is there.
    public static bool WheelAt(int x, int y, int notches, bool horizontal)
    {
        const uint WmMouseWheel = 0x020A, WmMouseHWheel = 0x020E;
        const int WheelStep = 120;
        var target = WindowQuery.WindowAt(x, y);
        if (target == IntPtr.Zero) return false;
        var delta = (short)Math.Clamp(notches * WheelStep, short.MinValue, short.MaxValue);
        var wParam = (UIntPtr)(uint)((ushort)delta << 16);
        var lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF)); // screen coordinates
        return PostMessage(target, horizontal ? WmMouseHWheel : WmMouseWheel, wParam, lParam);
    }

    public static void Restore(int x, int y, bool activeWindow = false) => With(x, y, activeWindow, h => ShowWindowAsync(h, SwRestore));

    public static void Close(int x, int y, bool activeWindow = false) => With(x, y, activeWindow, h => PostMessage(h, WmClose, UIntPtr.Zero, IntPtr.Zero));

    /// Makes the window under the pointer more (positive) or less (negative) see-through by <paramref name="percentStep"/> percent, never fully invisible.
    public static void AdjustOpacity(int x, int y, int percentStep) => With(x, y, false, h =>
    {
        var style = (long)GetWindowLongPtr(h, GwlExStyle);
        byte alpha = MaxAlpha;
        if ((style & ExLayered) != 0 && GetLayeredWindowAttributes(h, out _, out var current, out var flags) && (flags & LwaAlpha) != 0) alpha = current;
        else SetWindowLongPtr(h, GwlExStyle, new IntPtr(style | ExLayered)); // a window must be "layered" before it can be faded

        var next = Math.Clamp(alpha + percentStep * MaxAlpha / Models.Percent.Max, MinAlpha, MaxAlpha);
        SetLayeredWindowAttributes(h, 0, (byte)next, LwaAlpha);
    });

    public static void ToggleTopmost(int x, int y, bool activeWindow = false) => With(x, y, activeWindow, h =>
    {
        var onTop = ((long)GetWindowLongPtr(h, GwlExStyle) & ExTopmost) != 0;
        SetWindowPos(h, onTop ? HwndNoTopmost : HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpAsyncWindowPos);
    });

    /// True for the desktop and taskbar windows, which no mouse action should minimise, close, fade or drag.
    internal static bool IsProtected(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return ProtectedClasses.Contains(name.ToString());
    }

    private static void With(int x, int y, bool activeWindow, Action<IntPtr> act)
    {
        var hwnd = activeWindow ? WindowQuery.ActiveWindow() : WindowQuery.RootWindowAt(x, y);
        if (hwnd == IntPtr.Zero || IsProtected(hwnd)) return;
        act(hwnd);
    }
}
