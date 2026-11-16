using System.Runtime.InteropServices;
using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>How the pointer shows in a recording: the pointer itself, and a soft circle around it while a mouse button is down.</summary>
public sealed record PointerLook(bool ShowCursor, bool HighlightClicks, uint HighlightRgb, int HighlightRadius, double HighlightOpacity);

/// <summary>
/// Copies one area of the screen again and again for a recording (GDI, one device context and bitmap kept for the whole recording, released
/// on <see cref="Dispose"/>). The copy is made without CAPTUREBLT so the real pointer does not flicker; the pointer is drawn in instead,
/// as set. Windows marked "excluded from capture" (the recording bar and frame) are left out by Windows itself. Physical pixels.
/// </summary>
internal sealed unsafe class FrameGrabber : IDisposable
{
    private readonly ScreenRect _area;
    private readonly PointerLook _pointer;
    private readonly IntPtr _screen, _memory, _bitmap, _old;
    private readonly byte* _bits;
    private IntPtr _lastCursor;
    private int _hotX, _hotY;

    public int Width => _area.Width;
    public int Height => _area.Height;
    public int Stride => _area.Width * ScreenImage.BytesPerPixel;

    public FrameGrabber(ScreenRect area, PointerLook pointer)
    {
        _area = area;
        _pointer = pointer;
        _screen = GetDC(IntPtr.Zero);
        _memory = CreateCompatibleDC(_screen);
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = area.Width, Height = -area.Height, // negative: top row first
            Planes = 1, BitCount = 32,
        };
        _bitmap = CreateDIBSection(_screen, ref header, DibRgbColors, out var bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero)
        {
            DeleteDC(_memory);
            ReleaseDC(IntPtr.Zero, _screen);
            throw new InvalidOperationException("Windows could not make a bitmap for the recording.");
        }
        _bits = (byte*)bits;
        _old = SelectObject(_memory, _bitmap);
    }

    /// <summary>Copies the area now (with the pointer as set) and returns its pixels: BGRA, top row first, valid until the next grab.</summary>
    public ReadOnlySpan<byte> Grab()
    {
        BitBlt(_memory, 0, 0, _area.Width, _area.Height, _screen, _area.X, _area.Y, SrcCopy);
        GdiFlush();
        var pixels = new Span<byte>(_bits, Stride * _area.Height);
        if (_pointer.ShowCursor || _pointer.HighlightClicks) DrawPointer(pixels);
        return pixels;
    }

    private void DrawPointer(Span<byte> pixels)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info)) return;
        int x = info.Position.X - _area.X, y = info.Position.Y - _area.Y;
        if (_pointer.HighlightClicks && AnyButtonDown())
            Highlight(pixels, _area.Width, _area.Height, x, y, _pointer.HighlightRadius, _pointer.HighlightRgb, _pointer.HighlightOpacity);
        if (!_pointer.ShowCursor || (info.Flags & CursorShowing) == 0 || info.Cursor == IntPtr.Zero) return;
        if (info.Cursor != _lastCursor) ReadHotspot(info.Cursor);
        DrawIconEx(_memory, x - _hotX, y - _hotY, info.Cursor, 0, 0, 0, IntPtr.Zero, DiNormal);
        GdiFlush();
    }

    /// <summary>Where the pointer's tip is inside its picture (asked once per pointer shape; the two bitmaps Windows hands back are released).</summary>
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

    /// <summary>Blends a filled circle of the colour (0xRRGGBB) at the opacity into a BGRA picture, clipped to it.</summary>
    internal static void Highlight(Span<byte> pixels, int width, int height, int cx, int cy, int radius, uint rgb, double opacity)
    {
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        var keep = 1 - opacity;
        var reach = radius * radius;
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); y++)
        for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > reach) continue;
            var at = (y * width + x) * ScreenImage.BytesPerPixel;
            pixels[at] = (byte)(pixels[at] * keep + b * opacity);
            pixels[at + 1] = (byte)(pixels[at + 1] * keep + g * opacity);
            pixels[at + 2] = (byte)(pixels[at + 2] * keep + r * opacity);
        }
    }

    public void Dispose()
    {
        SelectObject(_memory, _old);
        DeleteObject(_bitmap);
        DeleteDC(_memory);
        ReleaseDC(IntPtr.Zero, _screen);
    }
}
