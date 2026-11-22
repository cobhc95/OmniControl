using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine;

/// Where a window was on screen (its top-left corner and size), so it can be put back exactly.
public readonly record struct WindowPlace(int Left, int Top, int Width, int Height);

/// The Windows 11 pop-up windows that may be Windows' own volume and brightness pop-up: top-level, untitled Explorer windows of class
/// XamlExplorerHostIslandWindow holding a DesktopWindowContentBridge "DesktopWindowXamlSource" with an InputSite child, owned by
/// explorer.exe or ShellExperienceHost.exe. Explorer uses the same kind of window for other surfaces too (the taskbar's thumbnail previews,
/// a full-screen host), so a window found here is only a candidate: <see cref="HasFlyoutShape"/> rejects anything that is not the size and
/// place of the volume pop-up, and which one it is is decided by the caller (<c>WindowsOsd</c>). Apps' own windows (ApplicationFrameWindow,
/// Windows.UI.Core.CoreWindow, WinUI's WinUIDesktopWin32WindowClass) can never be candidates: the class and the owning process must both match.
/// A window is hidden by moving it off screen (<see cref="Park"/>) and then minimising it (<see cref="Minimise"/>: a minimised host never
/// presents the pop-up, so nothing flashes); it is put back by un-minimising and hiding it while still off screen (<see cref="Unminimise"/>),
/// then moving it to where it was (<see cref="MoveTo"/>). Size, z-order and the foreground are never changed, every call is asynchronous and
/// nothing here waits on Explorer.
public static class WindowsOsdWindows
{
    private const string HostClass = "XamlExplorerHostIslandWindow";
    private const string BridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge", BridgeTitle = "DesktopWindowXamlSource";
    private const string InputSiteClass = "Windows.UI.Input.InputSite.WindowClass";

    /// Where a hidden pop-up is parked: Windows' own off-screen spot, on no monitor.
    private const int ParkAt = -32000;

    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsChild = 0x40000000, WsExAppWindow = 0x00040000;
    private const uint MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200, SwpAsyncWindowPos = 0x4000;
    private const int SwHide = 0, SwShowNoActivate = 4, SwShowMinNoActive = 7;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// Every candidate window that exists now (class, children, styles and owning program all match).
    public static List<IntPtr> Find()
    {
        var list = new List<IntPtr>();
        var host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, HostClass, string.Empty)) != IntPtr.Zero)
            if (HasCandidateShape(host)) list.Add(host);
        return list;
    }

    /// True when the window still is a candidate (it exists and is still the same kind of shell pop-up) and still belongs to process
    /// <paramref name="processId"/>: a window handle Windows has given to another window since is never touched.
    public static bool IsSameCandidate(IntPtr window, int processId) =>
        IsWindow(window) && ProcessOf(window) == processId && WindowQuery.ClassOf(window) == HostClass && GetWindowTextLength(window) == 0 && HasCandidateShape(window);

    /// True when the window is a candidate with the pop-up's size and place, and the pointer is not over the taskbar (a thumbnail preview).
    public static bool LooksLikeFlyout(IntPtr window) =>
        WindowQuery.ClassOf(window) == HostClass && GetWindowTextLength(window) == 0 && HasCandidateShape(window) && HasFlyoutShape(window) && !PointerOverShell();

    /// The process of Windows' shell (Explorer, which hosts the pop-up), or 0 before Explorer has started.
    public static int ShellProcess() => ProcessOf(GetShellWindow());

    /// The id of the process that owns the window, or 0.
    public static int ProcessOf(IntPtr window) => GetWindowThreadProcessId(window, out var pid) != 0 ? (int)pid : 0;

    /// True when the window is on screen: visible, not minimised and on a monitor (a parked window is not).
    public static bool IsOnScreen(IntPtr window)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || !GetWindowRect(window, out var r)) return false;
        return MonitorFromRect(ref r, MonitorDefaultToNull) != IntPtr.Zero;
    }

    /// True when the window is where <see cref="Park"/> puts it.
    public static bool IsParked(IntPtr window) => GetWindowRect(window, out var r) && r.Left == ParkAt && r.Top == ParkAt;

    /// The window's place, or null when it can't be read.
    public static WindowPlace? PlaceOf(IntPtr window) =>
        GetWindowRect(window, out var r) ? new WindowPlace(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;

    /// True when the window has the size and place of Windows' volume and brightness pop-up on its monitor: small (between
    /// <see cref="CoreTiming.WindowsOsdMinSidePx"/> and the <see cref="CoreTiming.WindowsOsdMaxWidthShare"/> and
    /// <see cref="CoreTiming.WindowsOsdMaxHeightShare"/> of the monitor), centred across it within <see cref="CoreTiming.WindowsOsdCentreShare"/>
    /// and near its bottom or top edge (within <see cref="CoreTiming.WindowsOsdEdgeShare"/>). A screen-sized host or a thumbnail preview
    /// beside a taskbar button fails this.
    public static bool HasFlyoutShape(IntPtr window)
    {
        if (!GetWindowRect(window, out var r)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref r, MonitorDefaultToNearest), ref info)) return false;
        var m = info.Monitor;
        double width = r.Right - r.Left, height = r.Bottom - r.Top;
        double screenWidth = m.Right - m.Left, screenHeight = m.Bottom - m.Top;
        if (width < CoreTiming.WindowsOsdMinSidePx || height < CoreTiming.WindowsOsdMinSidePx) return false;
        if (width > screenWidth * CoreTiming.WindowsOsdMaxWidthShare || height > screenHeight * CoreTiming.WindowsOsdMaxHeightShare) return false;
        var centreOff = Math.Abs((r.Left + r.Right) / 2.0 - (m.Left + m.Right) / 2.0);
        if (centreOff > screenWidth * CoreTiming.WindowsOsdCentreShare) return false;
        var edge = screenHeight * CoreTiming.WindowsOsdEdgeShare;
        return m.Bottom - r.Bottom <= edge || r.Top - m.Top <= edge;
    }

    /// Moves the window off screen without resizing, activating it or changing its z-order or show state; never waits on Explorer.
    public static void Park(IntPtr window) => Move(window, ParkAt, ParkAt);

    /// Moves the window back to <paramref name="place"/>'s corner, the same way as <see cref="Park"/>.
    public static void MoveTo(IntPtr window, WindowPlace place) => Move(window, place.Left, place.Top);

    /// True when the window is minimised.
    public static bool IsMinimised(IntPtr window) => IsIconic(window);

    /// Minimises the window without activating it; never waits on Explorer.
    public static void Minimise(IntPtr window) => ShowWindowAsync(window, SwShowMinNoActive);

    /// Un-minimises the window and hides it again (its idle state), queued in that order on Explorer's thread; it comes back to where it
    /// was parked, so nothing shows on screen.
    public static void Unminimise(IntPtr window)
    {
        ShowWindowAsync(window, SwShowNoActivate);
        ShowWindowAsync(window, SwHide);
    }

    private static void Move(IntPtr window, int x, int y) =>
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | SwpAsyncWindowPos);

    /// True when the pointer is over the taskbar or over one of these pop-ups: a pop-up that opens then is the taskbar's own (a thumbnail
    /// preview), never taken for the volume pop-up.
    public static bool PointerOverShell()
    {
        if (SystemCommands.GetCursorPos() is not { } at) return false;
        var className = WindowQuery.RootClassAt(at.X, at.Y);
        return PointerArea.IsTaskbar(className) || className == HostClass;
    }

    /// The window's handle, process and place for a log line ("0x1a2b (pid 812) at 100,200 300x80").
    public static string Describe(IntPtr window) =>
        PlaceOf(window) is { } p
            ? $"0x{window.ToInt64():x} (pid {ProcessOf(window)}) at {p.Left},{p.Top} {p.Width}x{p.Height}"
            : $"0x{window.ToInt64():x}";

    /// The checks that need no timing: the XAML-island children, a top-level window that is not listed on the taskbar, owned by the shell.
    private static bool HasCandidateShape(IntPtr host)
    {
        var bridge = FindWindowEx(host, IntPtr.Zero, BridgeClass, BridgeTitle);
        if (bridge == IntPtr.Zero || FindWindowEx(bridge, IntPtr.Zero, InputSiteClass, null) == IntPtr.Zero) return false;
        if ((GetWindowLongPtr(host, GwlStyle).ToInt64() & WsChild) != 0) return false;
        if ((GetWindowLongPtr(host, GwlExStyle).ToInt64() & WsExAppWindow) != 0) return false;
        var process = ProcessOf(host);
        return process != Environment.ProcessId && WindowQuery.IsShellProgram(process); // OmniControl's own windows are never candidates
    }
}
