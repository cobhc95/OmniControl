namespace OmniControl.Core.Engine.Pointer;

/// The one declaration of SystemParametersInfo and the mouse-related Windows calls around it, with the action codes OmniControl uses
/// (the touchpad's settings too, <c>Trackpad/TouchpadSettingsStore</c>). Each overload matches how the documentation says that action
/// passes its value: in uiParam, in pvParam itself, or through a pointer (a structure as bytes).
internal static class SpiNative
{
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool Set(uint action, uint uiParam, IntPtr pvParam, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool GetInt(uint action, uint uiParam, out int value, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool Array(uint action, uint uiParam, [In, Out] int[] values, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static extern bool Bytes(uint action, uint uiParam, [In, Out] byte[] buffer, uint flags);

    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadCursorFromFile(string fileName);

    [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, char[] buffer, uint size, IntPtr reserved);

    /// Save to the user's profile and tell every window, as Windows' own mouse settings do.
    public const uint SaveAndBroadcast = UpdateIniFile | SendChange;

    /// Tell every window but do not save: the change ends at sign-out.
    public const uint BroadcastOnly = SendChange;

    private const uint UpdateIniFile = 0x01, SendChange = 0x02;

    public const uint GetMouse = 0x0003, SetMouse = 0x0004;
    public const uint SetDoubleClickTime = 0x0020, SetMouseButtonSwap = 0x0021;
    public const uint SetCursors = 0x0057;
    public const uint SetMouseTrails = 0x005D, GetMouseTrails = 0x005E;
    public const uint GetSnapToDefButton = 0x005F, SetSnapToDefButton = 0x0060;
    public const uint GetWheelScrollLines = 0x0068, SetWheelScrollLines = 0x0069;
    public const uint GetWheelScrollChars = 0x006C, SetWheelScrollChars = 0x006D;
    public const uint GetMenuShowDelay = 0x006A;
    public const uint GetMouseSpeed = 0x0070, SetMouseSpeed = 0x0071;
    public const uint GetMouseSonar = 0x101C, SetMouseSonar = 0x101D;
    public const uint GetMouseClickLock = 0x101E, SetMouseClickLock = 0x101F;
    public const uint GetMouseVanish = 0x1020, SetMouseVanish = 0x1021;
    public const uint GetTouchpadParameters = 0x00AE, SetTouchpadParameters = 0x00AF;

    public const int SmSwapButton = 23;

    /// WHEEL_PAGESCROLL: the wheel scrolls one screen per notch instead of a number of lines.
    public const uint WheelPageScroll = uint.MaxValue;
}
