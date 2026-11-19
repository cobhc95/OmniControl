using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine;

/// Puts an overlay window at an exact rectangle in physical pixels, topmost and without activating it, and shows only the given parts
/// of it. Windows will not make a top-level window smaller than a minimum size, so a thin strip cannot be its own window; the window
/// covers a whole monitor and a window region cuts it down to the strips and labels, so nothing else is drawn or hit.
public static class OverlayPlacement
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    private const int RgnOr = 2;

    /// Puts <paramref name="window"/> at <paramref name="rect"/> and clips it to <paramref name="shown"/> (screen pixels). False when
    /// Windows refused (the window is gone).
    public static bool Place(IntPtr window, ScreenRect rect, IReadOnlyList<ScreenRect> shown)
    {
        if (!SetWindowPos(window, HwndTopmost, rect.Left, rect.Top, rect.Width, rect.Height, SwpNoActivate | SwpNoOwnerZOrder)) return false;
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var part in shown)
        {
            var piece = CreateRectRgn(part.Left - rect.Left, part.Top - rect.Top, part.Right - rect.Left, part.Bottom - rect.Top);
            CombineRgn(region, region, piece, RgnOr);
            DeleteObject(piece);
        }
        // On success the window owns the region; on failure it is still ours to release.
        if (SetWindowRgn(window, region, true) != 0) return true;
        DeleteObject(region);
        return false;
    }
}
