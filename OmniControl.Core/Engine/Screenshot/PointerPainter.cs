using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// How the pointer shows in a recording: the pointer itself, and a soft circle around it while a mouse button is down.
public sealed record PointerLook(bool ShowCursor, bool HighlightClicks, uint HighlightRgb, int HighlightRadius, double HighlightOpacity);

/// Draws the pointer, as set, into a recording's frame through a GDI device context, so the same drawing serves the GDI copy
/// (<see cref="FrameGrabber"/>) and the graphics-card copy (<see cref="DesktopDuplication"/>, through a GDI-compatible texture): the click
/// circle (a premultiplied picture made once and blended with AlphaBlend) while a button is down, then the pointer with DrawIconEx. The
/// pointer's hotspot is asked once per shape. Holds a small bitmap for the circle, released on <see cref="Dispose"/>.
internal sealed unsafe class PointerPainter : IDisposable
{
    private readonly PointerLook _look;
    private readonly IntPtr _circleDc, _circleBitmap, _circleOld;
    private readonly int _circleSide;
    private IntPtr _lastCursor;
    private int _hotX, _hotY;

    public PointerPainter(PointerLook look)
    {
        _look = look;
        if (!look.HighlightClicks) return;
        _circleSide = look.HighlightRadius * 2 + 1;
        var screen = GetDC(IntPtr.Zero);
        try
        {
            _circleBitmap = CreateTopDownBitmap(screen, _circleSide, _circleSide, out var bits);
            if (_circleBitmap == IntPtr.Zero) throw new InvalidOperationException("Windows could not make a bitmap for the recording.");
            _circleDc = CreateCompatibleDC(screen);
            _circleOld = SelectObject(_circleDc, _circleBitmap);
            Circle(new Span<byte>((void*)bits, _circleSide * _circleSide * ScreenImage.BytesPerPixel), look.HighlightRadius, look.HighlightRgb, look.HighlightOpacity);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// True when anything is drawn at all (otherwise the frame is used as copied).
    public bool Draws => _look.ShowCursor || _look.HighlightClicks;

    /// Draws the pointer into <paramref name="hdc"/>, whose top-left corner is the screen point (area.X, area.Y).
    public void Paint(IntPtr hdc, ScreenRect area)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info)) return;
        int x = info.Position.X - area.X, y = info.Position.Y - area.Y;
        if (_look.HighlightClicks && AnyButtonDown())
        {
            var r = _look.HighlightRadius;
            AlphaBlend(hdc, x - r, y - r, _circleSide, _circleSide, _circleDc, 0, 0, _circleSide, _circleSide, BlendPerPixelAlpha);
        }
        if (_look.ShowCursor && (info.Flags & CursorShowing) != 0 && info.Cursor != IntPtr.Zero)
        {
            if (info.Cursor != _lastCursor) ReadHotspot(info.Cursor);
            DrawIconEx(hdc, x - _hotX, y - _hotY, info.Cursor, 0, 0, 0, IntPtr.Zero, DiNormal);
        }
        GdiFlush();
    }

    /// The click circle as a premultiplied BGRA picture of side 2 x radius + 1: inside, the colour (0xRRGGBB) and alpha at the opacity;
    /// outside, nothing. Blended over a frame this gives frame x (1 - opacity) + colour x opacity inside the circle.
    internal static void Circle(Span<byte> pixels, int radius, uint rgb, double opacity)
    {
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        var side = radius * 2 + 1;
        var reach = radius * radius;
        pixels.Clear();
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            int dx = x - radius, dy = y - radius;
            if (dx * dx + dy * dy > reach) continue;
            var at = (y * side + x) * ScreenImage.BytesPerPixel;
            pixels[at] = (byte)(b * opacity);
            pixels[at + 1] = (byte)(g * opacity);
            pixels[at + 2] = (byte)(r * opacity);
            pixels[at + 3] = (byte)(255 * opacity);
        }
    }

    /// Where the pointer's tip is inside its picture (the two bitmaps Windows hands back are released).
    private void ReadHotspot(IntPtr cursor)
    {
        _lastCursor = cursor;
        _hotX = _hotY = 0;
        if (!GetIconInfo(cursor, out var icon)) return;
        _hotX = icon.HotspotX;
        _hotY = icon.HotspotY;
        if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask);
        if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color);
    }

    private static bool AnyButtonDown() =>
        (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0 || (GetAsyncKeyState(VkRightButton) & 0x8000) != 0 || (GetAsyncKeyState(VkMiddleButton) & 0x8000) != 0;

    public void Dispose()
    {
        if (_circleDc == IntPtr.Zero) return;
        SelectObject(_circleDc, _circleOld);
        DeleteObject(_circleBitmap);
        DeleteDC(_circleDc);
    }
}
