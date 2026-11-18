using System.Runtime.InteropServices;
using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.WindowNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>The pointer shapes the picture surface shows.</summary>
public enum SurfaceCursor { Cross, Move, Text, NorthSouth, WestEast, NorthWestSouthEast, NorthEastSouthWest }

/// <summary>
/// The area overlay's frozen picture as a plain Win32 child window, painted the way Lightshot paints it: an opaque window and two copies
/// of the picture made once (dimmed, and bright with the drawings); on every change only the changed rectangles are copied to the window
/// with GDI BitBlt, plus the selection's frame and handles. Nothing is blended, cleared or copied whole per frame, and Windows composes
/// only the changed area, on whichever graphics card drives the monitor. Coordinates are the window's physical pixels, which are the
/// picture's. Made and used on one thread (the UI thread); the pointer events are raised there.
/// </summary>
public sealed unsafe class PictureSurface : IDisposable
{
    private readonly WndProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private readonly string _className = "OmniControlPicture" + Guid.NewGuid().ToString("N"); // one class per surface: it holds this surface's procedure
    private readonly Action<string, Exception> _failed;
    private readonly PictureCopy _dim, _bright;
    private IntPtr _window, _edgeBrush, _cursor;
    private uint _edgeColor;
    private int _line = 1, _handle = 1;
    private ScreenRect? _selection;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    private int Stride => Width * ScreenImage.BytesPerPixel;
    private int Bytes => Stride * Height;
    private ScreenRect Bounds => new(0, 0, Width, Height);

    /// <summary>The left button went down (the surface then holds the pointer until it goes up), in picture pixels.</summary>
    public event Action<int, int>? Pressed;

    /// <summary>The pointer moved over the surface, or anywhere while the left button is held down.</summary>
    public event Action<int, int>? Moved;

    /// <summary>The left button went up.</summary>
    public event Action<int, int>? Released;

    /// <param name="failed">Told when something failed while the window handled a message (a failure must never cross into Windows).</param>
    public PictureSurface(int width, int height, Action<string, Exception> failed)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A picture needs at least one pixel.");
        Width = width;
        Height = height;
        _failed = failed;
        _proc = WindowProc;
        _dim = new PictureCopy(width, height);
        try
        {
            _bright = new PictureCopy(width, height);
        }
        catch (InvalidOperationException)
        {
            _dim.Dispose();
            throw;
        }
        _cursor = LoadCursor(IntPtr.Zero, IdcCross); // the first shape, before the pointer's first move picks one
    }

    /// <summary>Makes the window inside <paramref name="parent"/>, at its top-left corner and the picture's size.</summary>
    public IntPtr Create(IntPtr parent)
    {
        if (_window != IntPtr.Zero) return _window;
        var instance = GetModuleHandle(null);
        var wc = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), WndProc = Marshal.GetFunctionPointerForDelegate(_proc), Instance = instance, ClassName = _className };
        if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException($"Windows refused the screenshot overlay's window (error {Marshal.GetLastWin32Error()}).");
        _window = CreateWindowEx(0, _className, null, WsChild | WsVisible | WsClipSiblings, 0, 0, Width, Height, parent, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window != IntPtr.Zero) return _window;
        var error = Marshal.GetLastWin32Error();
        UnregisterClass(_className, instance);
        throw new InvalidOperationException($"Windows refused the screenshot overlay's window (error {error}).");
    }

    /// <summary>Closes the window (the picture copies stay until <see cref="Dispose"/>). Safe to call more than once.</summary>
    public void Destroy()
    {
        if (_window == IntPtr.Zero) return;
        DestroyWindow(_window);
        _window = IntPtr.Zero;
        UnregisterClass(_className, GetModuleHandle(null));
    }

    /// <summary>Writes the picture into the bright copy (no drawings yet) through <paramref name="write"/>: the copy's first byte and its bytes per row.</summary>
    public void ShowPicture(Action<IntPtr, int> write)
    {
        GdiFlush(); // GDI may still be reading the copy for a paint it batched
        write((IntPtr)_bright.Bits, Stride);
        Invalidate(Bounds);
    }

    /// <summary>Makes the dimmed copy: the picture (written through <paramref name="write"/>) under a wash of <paramref name="argb"/>.</summary>
    public void Dim(Action<IntPtr, int> write, uint argb)
    {
        GdiFlush();
        write((IntPtr)_dim.Bits, Stride);
        ScreenImage.Wash(new Span<byte>(_dim.Bits, Bytes), argb);
        Invalidate(Bounds);
    }

    /// <summary>Puts repainted pixels (the picture with its drawings) into the bright copy at <paramref name="area"/>; shown where the selection is.</summary>
    public void Paint(ScreenRect area, ScreenImage pixels)
    {
        var part = area.Intersect(Bounds);
        if (part.IsEmpty) return;
        GdiFlush();
        var row = part.Width * ScreenImage.BytesPerPixel;
        fixed (byte* source = pixels.Pixels)
            for (var y = 0; y < part.Height; y++)
                Buffer.MemoryCopy(source + (part.Y - area.Y + y) * pixels.Stride + (part.X - area.X) * ScreenImage.BytesPerPixel,
                    _bright.Bits + (part.Y + y) * Stride + part.X * ScreenImage.BytesPerPixel, row, row);
        if (_selection is { } selection) Invalidate(part.Intersect(selection));
    }

    /// <summary>The frame's colour (ARGB; drawn opaque), its line and its handles in physical pixels.</summary>
    public void SetLook(uint edgeArgb, int line, int handle)
    {
        line = Math.Max(1, line);
        handle = Math.Max(1, handle);
        if (_edgeBrush != IntPtr.Zero && edgeArgb == _edgeColor && line == _line && handle == _handle) return;
        var edges = SelectionFrame.Changed(_selection, null, _line, _handle); // where the old frame was
        _edgeColor = edgeArgb;
        _line = line;
        _handle = handle;
        if (_edgeBrush != IntPtr.Zero) DeleteObject(_edgeBrush);
        _edgeBrush = CreateSolidBrush((edgeArgb & 0xFF) << 16 | edgeArgb & 0xFF00 | (edgeArgb >> 16) & 0xFF); // COLORREF is 0x00BBGGRR
        foreach (var part in edges.Concat(SelectionFrame.Changed(null, _selection, _line, _handle))) Invalidate(part);
    }

    /// <summary>The selection (picture pixels; null or empty = none): only the strips that changed are painted again.</summary>
    public void SetSelection(ScreenRect? selection)
    {
        if (selection is { IsEmpty: true }) selection = null;
        if (selection == _selection) return;
        var changed = SelectionFrame.Changed(_selection, selection, _line, _handle);
        _selection = selection;
        foreach (var part in changed) Invalidate(part);
    }

    /// <summary>The pointer's shape over the surface from now on, shown at once (call it while the pointer is over the surface).</summary>
    public void ShowCursor(SurfaceCursor cursor)
    {
        _cursor = LoadCursor(IntPtr.Zero, cursor switch
        {
            SurfaceCursor.Move => IdcSizeAll,
            SurfaceCursor.Text => IdcIBeam,
            SurfaceCursor.NorthSouth => IdcSizeNs,
            SurfaceCursor.WestEast => IdcSizeWe,
            SurfaceCursor.NorthWestSouthEast => IdcSizeNwse,
            SurfaceCursor.NorthEastSouthWest => IdcSizeNesw,
            _ => IdcCross,
        });
        SetCursor(_cursor);
    }

    /// <summary>Paints what changed now, instead of when Windows next gets round to it.</summary>
    public void PaintNow()
    {
        if (_window != IntPtr.Zero) UpdateWindow(_window);
    }

    private void Invalidate(ScreenRect area)
    {
        area = area.Intersect(Bounds);
        if (_window == IntPtr.Zero || area.IsEmpty) return;
        var rect = new Rect { Left = area.X, Top = area.Y, Right = area.Right, Bottom = area.Bottom };
        InvalidateRect(_window, ref rect, false);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WmPaint:
                    Paint(hwnd);
                    return IntPtr.Zero;
                case WmEraseBackground:
                    return new IntPtr(1); // every pixel is painted from the copies, so nothing is cleared first (no flicker)
                case WmSetCursor when ((long)lParam & 0xFFFF) == HitClient:
                    SetCursor(_cursor);
                    return new IntPtr(1);
                case WmLeftButtonDown:
                    SetCapture(hwnd);
                    Pressed?.Invoke(X(lParam), Y(lParam));
                    return IntPtr.Zero;
                case WmMouseMove:
                    Moved?.Invoke(X(lParam), Y(lParam));
                    return IntPtr.Zero;
                case WmLeftButtonUp:
                    if (GetCapture() == hwnd) ReleaseCapture(); // first: the handler may close the overlay
                    Released?.Invoke(X(lParam), Y(lParam));
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception escaping a window procedure would take the whole app down; the step is dropped and the failure logged.
            _failed($"Screenshot overlay: window message {message}", ex);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static int X(IntPtr lParam) => (short)((long)lParam & 0xFFFF);
    private static int Y(IntPtr lParam) => (short)(((long)lParam >> 16) & 0xFFFF);

    /// <summary>Copies the dimmed picture outside the selection and the bright one inside it, then draws the frame; Windows clips it all to what changed.</summary>
    private void Paint(IntPtr hwnd)
    {
        var dc = BeginPaint(hwnd, out var paint);
        try
        {
            var area = new ScreenRect(paint.Paint.Left, paint.Paint.Top, paint.Paint.Right - paint.Paint.Left, paint.Paint.Bottom - paint.Paint.Top);
            if (_selection is not { } selection)
            {
                Blit(dc, area, _dim);
                return;
            }
            var saved = SaveDC(dc);
            ExcludeClipRect(dc, selection.X, selection.Y, selection.Right, selection.Bottom);
            Blit(dc, area, _dim);
            RestoreDC(dc, saved);
            Blit(dc, area.Intersect(selection), _bright);
            foreach (var part in SelectionFrame.Line(selection, _line).Concat(SelectionFrame.Handles(selection, _handle)))
            {
                var rect = new Rect { Left = part.X, Top = part.Y, Right = part.Right, Bottom = part.Bottom };
                FillRect(dc, ref rect, _edgeBrush);
            }
        }
        finally
        {
            EndPaint(hwnd, ref paint);
        }
    }

    private void Blit(IntPtr dc, ScreenRect area, PictureCopy from)
    {
        area = area.Intersect(Bounds);
        if (!area.IsEmpty) BitBlt(dc, area.X, area.Y, area.Width, area.Height, from.Dc, area.X, area.Y, SrcCopy);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Destroy();
        _dim.Dispose();
        _bright.Dispose();
        if (_edgeBrush != IntPtr.Zero) DeleteObject(_edgeBrush);
        _edgeBrush = IntPtr.Zero;
    }

    /// <summary>One copy of the picture: a bitmap the program writes into, selected into a memory DC that the window copies from.</summary>
    private sealed class PictureCopy : IDisposable
    {
        private readonly IntPtr _bitmap, _old;

        public IntPtr Dc { get; }
        public byte* Bits { get; }

        public PictureCopy(int width, int height)
        {
            var screen = GetDC(IntPtr.Zero);
            try
            {
                _bitmap = CreateTopDownBitmap(screen, width, height, out var bits);
                if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("Windows could not make a bitmap for the screenshot overlay.");
                Bits = (byte*)bits;
                Dc = CreateCompatibleDC(screen);
                _old = SelectObject(Dc, _bitmap);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public void Dispose()
        {
            SelectObject(Dc, _old);
            DeleteDC(Dc);
            DeleteObject(_bitmap);
        }
    }
}
