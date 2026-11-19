using OmniControl.Core.Engine.Mouse;
using static OmniControl.Core.Native.ScreenshotNative;

namespace OmniControl.Core.Engine.Screenshot;

/// One monitor: where it sits on the virtual desktop, in physical pixels.
public sealed record ScreenMonitor(ScreenRect Bounds, bool IsPrimary);

/// The active window: its visible frame on screen and the name of the program that owns it ("chrome").
public sealed record ActiveWindowInfo(ScreenRect Bounds, string App);

/// Copies what is on screen. Every rectangle is in physical pixels on the virtual desktop (the process is per-monitor DPI aware, so
/// Windows reports real pixels on every monitor whatever its scale). Each copy releases its device contexts and bitmap before it returns.
public static class ScreenCapture
{
    /// Every monitor, the primary one first.
    public static IReadOnlyList<ScreenMonitor> Monitors()
    {
        var found = new List<ScreenMonitor>();
        bool Add(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) found.Add(new ScreenMonitor(ToRect(info.Monitor), (info.Flags & MonitorPrimary) != 0));
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Add, IntPtr.Zero);
        return found.OrderByDescending(m => m.IsPrimary).ToList();
    }

    /// The whole virtual desktop: every monitor's area together.
    public static ScreenRect VirtualScreen() => new(GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

    /// The monitor the mouse pointer is on (the nearest one when it is between monitors).
    public static ScreenRect MonitorUnderPointer()
    {
        GetCursorPos(out var point);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromPoint(point, MonitorDefaultToNearest), ref info) ? ToRect(info.Monitor) : VirtualScreen();
    }

    /// The active window's visible frame (without the invisible resize border) and its program's name; null when there is none.
    public static ActiveWindowInfo? ActiveWindow()
    {
        var window = WindowQuery.ActiveWindow();
        if (window == IntPtr.Zero) return null;
        var bounds = DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>()) == 0
            ? frame
            : GetWindowRect(window, out var rect) ? rect : default;
        var area = ToRect(bounds).Intersect(VirtualScreen());
        return area.IsEmpty ? null : new ActiveWindowInfo(area, ProgramName(WindowQuery.ProcessOf(window)));
    }

    /// The name of the program that owns the active window ("chrome"), or empty.
    public static string ActiveApp() => ProgramName(WindowQuery.ProcessOf(WindowQuery.ActiveWindow()));

    private static string ProgramName(int? processId)
    {
        if (processId is not { } id) return string.Empty;
        try
        {
            using var process = Process.GetProcessById(id);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return string.Empty; // the program closed meanwhile: the file name just has no app part
        }
    }

    /// A copy of the screen inside <paramref name="area"/> (layered windows included), every pixel opaque.
    public static ScreenImage Capture(ScreenRect area) => Capture(area, (bits, stride) =>
    {
        var pixels = new byte[stride * area.Height];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        ScreenImage.MakeOpaque(pixels);
        return new ScreenImage(area.Width, area.Height, pixels);
    });

    /// Copies the screen inside <paramref name="area"/> (layered windows included) and hands the copy to <paramref name="use"/>: BGRA, top
    /// row first, the given bytes per row, alpha not set. The pixels are valid only while <paramref name="use"/> runs, so a caller that
    /// keeps its own copy (the area overlay's bitmap) makes it straight from Windows' bitmap with no picture in between.
    public static T Capture<T>(ScreenRect area, Func<IntPtr, int, T> use)
    {
        if (area.IsEmpty) throw new ArgumentException("Nothing to capture.", nameof(area));
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateTopDownBitmap(screen, area.Width, area.Height, out var bits);
        try
        {
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("Windows could not make a bitmap for the screenshot.");
            var old = SelectObject(memory, bitmap);
            var copied = BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy | CaptureBlt);
            SelectObject(memory, old);
            if (!copied) throw new InvalidOperationException("Windows did not allow the screen to be copied.");
            return use(bits, area.Width * ScreenImage.BytesPerPixel);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static ScreenRect ToRect(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
