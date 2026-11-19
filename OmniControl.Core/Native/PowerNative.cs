namespace OmniControl.Core.Native;

/// user32 and kernel32 power calls for the power journal: the power-setting notices a window registers for (the display turning off and on,
/// which is how Modern Standby shows itself), the broadcast values of <c>WM_POWERBROADCAST</c>, and the clock that stops while the PC sleeps.
internal static class PowerNative
{
    public const uint WmPowerBroadcast = 0x0218, WmEndSession = 0x0016;
    public const int PbtSuspend = 0x0004, PbtResumeSuspend = 0x0007, PbtResumeAutomatic = 0x0012, PbtPowerSettingChange = 0x8013;

    /// The <c>lParam</c> bit of <see cref="WmEndSession"/> that says the user logs off (otherwise Windows shuts down or restarts).
    public const long EndSessionLogoff = 0x80000000;

    private const int DeviceNotifyWindowHandle = 0;

    /// The console's display state: 0 off, 1 on, 2 dimmed (Windows 8 and later; on a Modern Standby PC "off" is how standby starts).
    public static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// Whether the monitors are powered: 0 off, 1 on (the older notice, still sent).
    public static readonly Guid MonitorPowerOn = new("02731015-4510-4526-99E6-E5A17EBD1AEA");

    /// The fixed head of a <c>POWERBROADCAST_SETTING</c>: the setting's GUID and the length of the data after it.
    [StructLayout(LayoutKind.Sequential)]
    public struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    /// Asks Windows to send <paramref name="window"/> a <see cref="PbtPowerSettingChange"/> when the setting changes; zero when refused.
    public static IntPtr RegisterForWindow(IntPtr window, Guid setting) => RegisterPowerSettingNotification(window, ref setting, DeviceNotifyWindowHandle);

    /// The setting and its first 32-bit value from a <see cref="PbtPowerSettingChange"/>'s <c>lParam</c>; null when it carries none.
    public static (Guid Setting, int Value)? ReadSetting(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return null;
        var head = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
        if (head.DataLength < sizeof(int)) return null;
        return (head.PowerSetting, Marshal.ReadInt32(lParam, Marshal.SizeOf<PowerBroadcastSetting>()));
    }

    /// Interrupt time without the time spent asleep or hibernating, in 100-ns units.
    [DllImport("kernel32.dll")]
    public static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
