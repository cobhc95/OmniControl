namespace OmniControl.Core.Engine;

/// Read-only questions about the machine's power and activity: on battery or plugged in, and how long nobody has used the keyboard or mouse.
public static class PowerState
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Time; }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();

    private const byte OnBatteryLine = 0, PluggedLine = 1, NoBattery = 128, ChargingFlag = 8;
    private const uint UnknownTime = 0xFFFFFFFF;

    /// True on battery, false when plugged in, null when Windows can't say (a desktop PC reports plugged in).
    public static bool? IsOnBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return status.AcLineStatus switch { OnBatteryLine => true, PluggedLine => false, _ => null };
    }

    /// The battery's charge in percent, or null when there is no battery or Windows can't say.
    public static int? BatteryPercent()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryLifePercent > 100 || (status.BatteryFlag & NoBattery) != 0) return null;
        return status.BatteryLifePercent;
    }

    /// The battery as Windows reports it: charge, whether it is plugged in, whether it is charging, and the time left on battery when Windows knows it. Null when there is no battery.
    public static BatteryStatus? Battery()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryLifePercent > 100 || (status.BatteryFlag & NoBattery) != 0) return null;
        var left = status.BatteryLifeTime == UnknownTime ? (TimeSpan?)null : TimeSpan.FromSeconds(status.BatteryLifeTime);
        return new BatteryStatus(status.BatteryLifePercent, status.AcLineStatus == PluggedLine, (status.BatteryFlag & ChargingFlag) != 0, left);
    }

    /// True when the PC has a battery (a laptop or tablet); false when there is none or Windows can't say.
    public static bool HasBattery() => GetSystemPowerStatus(out var status) && (status.BatteryFlag & NoBattery) == 0 && status.BatteryFlag != 255;

    /// How long it is since the last key press or mouse input (any program), or zero when Windows can't say.
    public static TimeSpan IdleFor()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked(GetTickCount() - info.Time)) : TimeSpan.Zero;
    }
}

/// One reading of the battery: <see cref="Percent"/> charge, <see cref="Plugged"/> in, <see cref="Charging"/>, and <see cref="Left"/> on battery (null when Windows does not know).
public sealed record BatteryStatus(int Percent, bool Plugged, bool Charging, TimeSpan? Left);
