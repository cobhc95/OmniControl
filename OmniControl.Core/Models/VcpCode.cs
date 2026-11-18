namespace OmniControl.Core.Models;

/// <summary>
/// The VESA MCCS VCP codes OmniControl reads and writes. A monitor only answers the codes it implements; which ones it does is
/// found per monitor (<see cref="MonitorCapabilities"/>), never assumed.
/// </summary>
public static class VcpCode
{
    public const byte Brightness  = 0x10; // continuous
    public const byte Contrast    = 0x12; // continuous
    public const byte ColorPreset = 0x14; // table: sRGB, 6500 K, user 1...
    public const byte RedGain     = 0x16; // continuous
    public const byte GreenGain   = 0x18; // continuous
    public const byte BlueGain    = 0x1A; // continuous
    public const byte InputSource = 0x60; // table: HDMI 1, DisplayPort 1...
    public const byte SpeakerVolume = 0x62; // continuous
    public const byte Sharpness   = 0x87; // continuous
    public const byte Saturation  = 0x8A; // continuous
    public const byte PowerMode   = 0xD6; // table: on, standby, off

    /// <summary>Values of <see cref="PowerMode"/>. Off is the "soft" off a monitor leaves again on its own button or on input.</summary>
    public static class Power
    {
        public const uint On = 0x01;
        public const uint Off = 0x04;

        /// <summary>Standby (2), suspend (3) and off (4, 5) are all a screen that is not showing; on is only 1. An unknown value is not called off.</summary>
        public static bool IsOff(uint value) => value is >= 2 and <= 5;
    }
}
