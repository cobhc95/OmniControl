using OmniControl.Core.Engine.Pointer;

namespace OmniControl.Core.Engine.Mouse;

/// Whole-system actions a mouse button can trigger: monitors off, screensaver, pointer speed.
public static class SystemCommands
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    private const uint WmSysCommand = 0x0112, ScMonitorPower = 0xF170, ScScreenSave = 0xF140;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const int MonitorOff = 2;

    /// Pointer speed on Windows' 1 (slowest) to 20 scale.
    public static int MinSpeed => PointerValues.Info(PointerValue.Speed).Min;
    public static int MaxSpeed => PointerValues.Info(PointerValue.Speed).Max;

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool SetCursorPosition(int x, int y);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool GetCursorPosition(out Point p);

    /// Puts the pointer at a screen position.
    public static void SetCursorPos(int x, int y) => SetCursorPosition(x, y);

    /// Where the pointer is now, or null when Windows won't say (a secure desktop is showing).
    public static (int X, int Y)? GetCursorPos() => GetCursorPosition(out var p) ? (p.X, p.Y) : null;

    private const int SmCxDoubleClick = 36, SmCyDoubleClick = 37;

    /// Windows' own double-click rule: the most milliseconds between the two presses, and how far apart (width, height in pixels)
    /// they may be. The user sets the time in the mouse settings.
    public static (int Milliseconds, int Width, int Height) DoubleClick() =>
        ((int)SpiNative.GetDoubleClickTime(), SpiNative.GetSystemMetrics(SmCxDoubleClick), SpiNative.GetSystemMetrics(SmCyDoubleClick));

    /// Windows' menu show delay (how long the pointer rests on a sub-menu row before it opens), or null when Windows won't say.
    public static int? MenuShowDelay() => SpiNative.GetInt(SpiNative.GetMenuShowDelay, 0, out var ms, 0) ? ms : null;

    public static void TurnMonitorsOff() => PostMessage(HwndBroadcast, WmSysCommand, (UIntPtr)ScMonitorPower, (IntPtr)MonitorOff);

    public static void StartScreensaver() => PostMessage(GetDesktopWindow(), WmSysCommand, (UIntPtr)ScScreenSave, IntPtr.Zero);

    /// The pointer speed now (1-20), or null when Windows won't say.
    public static int? GetCursorSpeed() => SpiNative.GetInt(SpiNative.GetMouseSpeed, 0, out var speed, 0) ? speed : null;

    /// Sets the pointer speed for this session only (it is not written to the user's settings).
    public static void SetCursorSpeed(int speed) =>
        SpiNative.Set(SpiNative.SetMouseSpeed, 0, (IntPtr)Math.Clamp(speed, MinSpeed, MaxSpeed), 0);
}
