using static OmniControl.Core.Native.D3D11Native;
using static OmniControl.Core.Native.MediaFoundationNative;
using Gdi = OmniControl.Core.Native.ScreenshotNative;

namespace OmniControl.Core.Engine.Screenshot;

/// A frame that lives on the graphics card: the device it was made on, and a copy of it into a texture of the same size.
internal interface IGpuFrame
{
    /// The Direct3D 11 device (ID3D11Device) the frame's textures belong to.
    IntPtr Device { get; }

    /// Copies the current frame into <paramref name="texture"/> (an ID3D11Texture2D of the area's size, BGRA) on the graphics card.
    void CopyTo(IntPtr texture);
}

/// Copies one area of one monitor for an MP4 recording with DXGI Desktop Duplication, so the picture stays on the graphics card: the
/// monitor's image is copied (only the area) into a texture, the pointer is drawn into a second, GDI-compatible texture with the same
/// <see cref="PointerPainter"/> as the GDI copy, and the encoder takes that texture (<see cref="IGpuFrame"/>). A frame in which nothing on
/// the monitor changed keeps the last picture. When Windows takes the duplication away (a mode change, the secure desktop), the area is
/// copied with GDI into the same texture until it can be duplicated again (retried every <see cref="CoreTiming.RecordDuplicationRetryMs"/>),
/// said once in the log. Made only by <see cref="TryOpen"/>; every object is released on <see cref="Dispose"/>. Recording thread only.
internal sealed class DesktopDuplication : IGpuFrame, IDisposable
{
    private readonly ScreenRect _area;
    private readonly PointerPainter _pointer;
    private readonly RecordingLog _log;
    private IntPtr _device, _context, _output, _duplication, _desktop, _desktopSurface, _frame, _frameSurface, _screen;
    private Box _box;
    private long _retryAt;
    private bool _lossLogged;

    public IntPtr Device => _device;

    /// The LUID of the adapter that drives the monitor (and so holds the device, the textures and the encoder's device manager).
    public long AdapterLuid { get; private set; }

    private DesktopDuplication(ScreenRect area, PointerLook pointer, RecordingLog log)
    {
        _area = area;
        _pointer = new PointerPainter(pointer);
        _log = log;
    }

    /// Opens the duplication of the monitor that holds the whole <paramref name="area"/>, on the graphics card that drives it. Null, with
    /// the reason in <paramref name="why"/>, when that cannot be done (the area spans monitors, a turned monitor, no Direct3D 11 video
    /// support, Windows refused); the caller then records with GDI.
    public static DesktopDuplication? TryOpen(ScreenRect area, PointerLook pointer, RecordingLog log, out string? why)
    {
        var capture = new DesktopDuplication(area, pointer, log);
        try
        {
            why = capture.Open();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            why = ex.Message;
        }
        if (why is null) return capture;
        capture.Dispose();
        return null;
    }

    /// Finds the output and its adapter, makes the device, the duplication and the two textures. Null when done, else why not.
    private unsafe string? Open()
    {
        var iid = IidFactory1;
        Check(CreateDXGIFactory1(ref iid, out var factory));
        IntPtr adapter;
        OutputDesc desktop;
        try
        {
            (adapter, _output, desktop) = FindOutput(factory);
        }
        finally
        {
            Release(factory);
        }
        if (adapter == IntPtr.Zero) return "the area is not inside one monitor";
        try
        {
            var owner = GetAdapterDesc1(adapter);
            AdapterLuid = owner.Luid;
            _log.Line($"monitor {new string(desktop.DeviceName)} is driven by \"{new string(owner.Description)}\" LUID {(uint)(owner.Luid >> 32):X8}:{(uint)owner.Luid:X8}; duplication, colour conversion and encoder device all go on it");
            if (desktop.Rotation is not (RotationIdentity or RotationUnspecified)) return "the monitor is turned";
            _box = BoxOf(desktop.Desktop);
            Check(D3D11CreateDevice(adapter, DriverTypeUnknown, IntPtr.Zero, CreateBgraSupport | CreateVideoSupport, IntPtr.Zero, 0, SdkVersion,
                out _device, out _, out _context));
        }
        finally
        {
            Release(adapter);
        }
        var multithread = As(_device, IidMultithread);
        if (multithread == IntPtr.Zero) return "the graphics card's device cannot be shared with the encoder";
        SetMultithreadProtected(multithread, true); // the encoder uses the same device from its own threads
        Release(multithread);
        string? refused;
        try
        {
            refused = Duplicate();
        }
        catch (UnauthorizedAccessException) // E_ACCESSDENIED; any other refusal goes to the caller, which records with GDI
        {
            refused = null; // the secure desktop (locked, UAC) refuses it for now: GDI copies into the textures until it can be duplicated again
            Lost();
        }
        if (refused is not null) return refused;
        (_desktop, _desktopSurface) = NewTexture();
        (_frame, _frameSurface) = NewTexture();
        _screen = Gdi.GetDC(IntPtr.Zero);
        CopyWithGdi(); // the first picture, so a frame is never empty before the first duplicated one arrives
        return null;
    }

    /// The adapter (AddRef'd) and IDXGIOutput1 of the attached monitor whose desktop holds the whole area; zeros when none.
    private unsafe (IntPtr Adapter, IntPtr Output1, OutputDesc Desc) FindOutput(IntPtr factory)
    {
        for (uint a = 0; EnumAdapters1(factory, a, out var adapter) == 0; a++)
        {
            try
            {
                var (output1, desc) = OutputOf(adapter);
                if (output1 != IntPtr.Zero) return (adapter, output1, desc);
            }
            catch
            {
                Release(adapter);
                throw;
            }
            Release(adapter);
        }
        return (IntPtr.Zero, IntPtr.Zero, default);
    }

    /// The IDXGIOutput1 of the adapter's attached monitor that holds the whole area; zero when it has none.
    private (IntPtr Output1, OutputDesc Desc) OutputOf(IntPtr adapter)
    {
        for (uint o = 0; EnumOutputs(adapter, o, out var output) == 0; o++)
        {
            try
            {
                var desc = GetOutputDesc(output);
                if (desc.AttachedToDesktop == 0 || !Holds(desc.Desktop)) continue;
                var output1 = As(output, IidOutput1);
                if (output1 != IntPtr.Zero) return (output1, desc);
            }
            finally
            {
                Release(output);
            }
        }
        return (IntPtr.Zero, default);
    }

    private bool Holds(Gdi.Rect monitor) =>
        _area.X >= monitor.Left && _area.Y >= monitor.Top && _area.X + _area.Width <= monitor.Right && _area.Y + _area.Height <= monitor.Bottom;

    private Box BoxOf(Gdi.Rect monitor)
    {
        uint left = (uint)(_area.X - monitor.Left), top = (uint)(_area.Y - monitor.Top);
        return new Box { Left = left, Top = top, Front = 0, Right = left + (uint)_area.Width, Bottom = top + (uint)_area.Height, Back = 1 };
    }

    /// Starts duplicating the output (again). Null when done, else why not.
    private string? Duplicate()
    {
        var desktop = GetOutputDesc(_output);
        if (desktop.AttachedToDesktop == 0 || !Holds(desktop.Desktop)) return "the area is no longer inside the monitor";
        var duplication = DuplicateOutput(_output, _device);
        var desc = GetDuplicationDesc(duplication);
        _log.Line($"duplication {desc.Width}x{desc.Height} at {(desc.RefreshDenominator == 0 ? 0 : (double)desc.RefreshNumerator / desc.RefreshDenominator):0.##} Hz, format {desc.Format}, image in {(desc.DesktopImageInSystemMemory != 0 ? "system memory" : "video memory")}");
        if (desc.Rotation is not (RotationIdentity or RotationUnspecified) || desc.DesktopImageInSystemMemory != 0)
        {
            Release(duplication);
            return desc.DesktopImageInSystemMemory != 0 ? "Windows keeps this monitor's image in system memory" : "the monitor is turned";
        }
        _box = BoxOf(desktop.Desktop);
        _duplication = duplication;
        return null;
    }

    /// A BGRA texture of the area's size that GDI can draw on, and its IDXGISurface1.
    private (IntPtr Texture, IntPtr Surface) NewTexture()
    {
        var texture = CreateTexture2D(_device, new Texture2DDesc
        {
            Width = (uint)_area.Width, Height = (uint)_area.Height, MipLevels = 1, ArraySize = 1, Format = FormatB8G8R8A8, SampleCount = 1,
            Usage = UsageDefault, BindFlags = BindRenderTarget | BindShaderResource, MiscFlags = MiscGdiCompatible,
        });
        var surface = As(texture, IidSurface1);
        if (surface != IntPtr.Zero) return (texture, surface);
        Release(texture);
        throw new InvalidOperationException("the graphics card's texture cannot be drawn on with GDI");
    }

    /// Brings the frame up to date: the monitor's latest picture (or a GDI copy while the duplication is lost), then the pointer.
    public void Grab()
    {
        if (_duplication == IntPtr.Zero && Environment.TickCount64 >= _retryAt) TryDuplicateAgain();
        if (_duplication != IntPtr.Zero) Acquire();
        else CopyWithGdi();
        if (!_pointer.Draws) return;
        CopyResource(_context, _frame, _desktop);
        var hdc = GetDC(_frameSurface);
        try
        {
            _pointer.Paint(hdc, _area);
        }
        finally
        {
            ReleaseDC(_frameSurface);
        }
    }

    public void CopyTo(IntPtr texture) => CopyResource(_context, texture, _pointer.Draws ? _frame : _desktop);

    private void Acquire()
    {
        var hr = AcquireNextFrame(_duplication, 0, out var resource);
        if (hr == ErrorWaitTimeout) return; // nothing changed: the last picture stands
        if (hr is ErrorAccessLost or AccessDenied)
        {
            Lost();
            CopyWithGdi();
            return;
        }
        Check(hr);
        try
        {
            var texture = As(resource, IidTexture2D);
            if (texture == IntPtr.Zero) return;
            CopySubresourceRegion(_context, _desktop, texture, _box);
            Release(texture);
        }
        finally
        {
            Release(resource);
            ReleaseFrame(_duplication);
        }
    }

    private void Lost()
    {
        Release(_duplication);
        _duplication = IntPtr.Zero;
        _retryAt = Environment.TickCount64 + CoreTiming.RecordDuplicationRetryMs;
        if (_lossLogged) return;
        _lossLogged = true;
        _log.Line("Windows stopped the graphics-card copy of the screen (a display change or the secure desktop); frames are copied with GDI until it is back.");
    }

    private void TryDuplicateAgain()
    {
        try
        {
            if (Duplicate() is null) return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException) // E_ACCESSDENIED comes as UnauthorizedAccessException
        {
            // still refused (the secure desktop is up, the mode is changing): GDI copies go on and it is tried again later
        }
        _retryAt = Environment.TickCount64 + CoreTiming.RecordDuplicationRetryMs;
    }

    /// Copies the area with GDI straight into the desktop texture (the first picture, and while the duplication is lost).
    private void CopyWithGdi()
    {
        var hdc = GetDC(_desktopSurface);
        try
        {
            Gdi.BitBlt(hdc, 0, 0, _area.Width, _area.Height, _screen, _area.X, _area.Y, Gdi.SrcCopy);
        }
        finally
        {
            ReleaseDC(_desktopSurface);
        }
    }

    public void Dispose()
    {
        _pointer.Dispose();
        if (_screen != IntPtr.Zero) Gdi.ReleaseDC(IntPtr.Zero, _screen);
        foreach (var obj in new[] { _frameSurface, _frame, _desktopSurface, _desktop, _duplication, _output, _context, _device }) Release(obj);
        _screen = _frameSurface = _frame = _desktopSurface = _desktop = _duplication = _output = _context = _device = IntPtr.Zero;
    }
}
