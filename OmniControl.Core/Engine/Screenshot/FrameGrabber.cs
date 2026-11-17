using System.Runtime.InteropServices;
using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// Copies one area of the screen again and again for a recording (GDI, one device context and bitmap kept for the whole recording, released
/// on <see cref="Dispose"/>): the GIF's frames, and the MP4's when the graphics-card copy (<see cref="DesktopDuplication"/>) is not
/// available. The copy is made without CAPTUREBLT so the real pointer does not flicker; the pointer is drawn in instead
/// (<see cref="PointerPainter"/>). Windows marked "excluded from capture" (the recording bar and frame) are left out by Windows itself.
/// Physical pixels.
/// </summary>
internal sealed unsafe class FrameGrabber : IDisposable
{
    private readonly ScreenRect _area;
    private readonly PointerPainter _pointer;
    private readonly IntPtr _screen, _memory, _bitmap, _old;
    private readonly byte* _bits;

    public int Stride => _area.Width * ScreenImage.BytesPerPixel;

    public FrameGrabber(ScreenRect area, PointerLook pointer)
    {
        _area = area;
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
        _pointer = new PointerPainter(pointer);
    }

    /// <summary>Copies the area now (with the pointer as set) and returns its pixels: BGRA, top row first, valid until the next grab.</summary>
    public ReadOnlySpan<byte> Grab()
    {
        BitBlt(_memory, 0, 0, _area.Width, _area.Height, _screen, _area.X, _area.Y, SrcCopy);
        GdiFlush();
        if (_pointer.Draws) _pointer.Paint(_memory, _area);
        return new ReadOnlySpan<byte>(_bits, Stride * _area.Height);
    }

    public void Dispose()
    {
        _pointer.Dispose();
        SelectObject(_memory, _old);
        DeleteObject(_bitmap);
        DeleteDC(_memory);
        ReleaseDC(IntPtr.Zero, _screen);
    }
}
