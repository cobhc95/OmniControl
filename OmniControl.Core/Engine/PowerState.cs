using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>Read-only questions about the machine's power and activity: on battery or plugged in, and how long nobody has used the keyboard or mouse.</summary>
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

    private const byte OnBatteryLine = 0, PluggedLine = 1, NoBattery = 128;

    /// <summary>True on battery, false when plugged in, null when Windows can't say (a desktop PC reports plugged in).</summary>
    public static bool? IsOnBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return status.AcLineStatus switch { OnBatteryLine => true, PluggedLine => false, _ => null };
    }

    /// <summary>The battery's charge in percent, or null when there is no battery or Windows can't say.</summary>
    public static int? BatteryPercent()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryLifePercent > 100 || (status.BatteryFlag & NoBattery) != 0) return null;
        return status.BatteryLifePercent;
    }

    /// <summary>True when the PC has a battery (a laptop or tablet); false when there is none or Windows can't say.</summary>
    public static bool HasBattery() => GetSystemPowerStatus(out var status) && (status.BatteryFlag & NoBattery) == 0 && status.BatteryFlag != 255;

    /// <summary>How long it is since the last key press or mouse input (any program), or zero when Windows can't say.</summary>
    public static TimeSpan IdleFor()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked(GetTickCount() - info.Time)) : TimeSpan.Zero;
    }
}
