namespace OmniControl.Core.Engine.Mouse;

/// Moves or resizes the window under the pointer as the pointer moves (the way Alt-drag works on other systems).
public sealed class WindowDrag
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpAsyncWindowPos = 0x4000; // async: never wait on the dragged app
    private const int MinSize = 120; // a window dragged smaller than this is too small to grab again

    private IntPtr _window;
    private bool _resize;
    private int _startX, _startY;
    private Rect _start;

    /// True while a drag is going on.
    public bool IsActive => _window != IntPtr.Zero;

    /// Starts dragging the window under (<paramref name="x"/>, <paramref name="y"/>); false when there is nothing sensible to drag (desktop, taskbar, maximised window).
    public bool Begin(int x, int y, bool resize)
    {
        var window = WindowQuery.RootWindowAt(x, y);
        if (window == IntPtr.Zero || WindowCommands.IsProtected(window) || IsZoomed(window) || !GetWindowRect(window, out _start)) return false;

        (_window, _resize, _startX, _startY) = (window, resize, x, y);
        return true;
    }

    /// Follows the pointer to (<paramref name="x"/>, <paramref name="y"/>).
    public void Move(int x, int y)
    {
        if (!IsActive) return;
        var dx = x - _startX;
        var dy = y - _startY;
        if (_resize)
            SetWindowPos(_window, IntPtr.Zero, _start.Left, _start.Top,
                Math.Max(MinSize, _start.Right - _start.Left + dx), Math.Max(MinSize, _start.Bottom - _start.Top + dy), SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos);
        else
            SetWindowPos(_window, IntPtr.Zero, _start.Left + dx, _start.Top + dy, 0, 0, SwpNoZOrder | SwpNoActivate | SwpNoSize | SwpAsyncWindowPos);
    }

    public void End() => _window = IntPtr.Zero;
}
