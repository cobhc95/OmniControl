namespace OmniDial.Core.Models;

/// <summary>
/// VESA MCCS VCP codes. Only a subset is hardware-safe / commonly supported.
/// Codes follow the MCCS spec; values are raw DDC/CI values, range-checked against
/// the monitor's reported maximum at runtime.
/// </summary>
public static class VcpCode
{
    public const byte Brightness       = 0x10; // RW, typical 0-100
    public const byte Contrast         = 0x12; // RW, typical 0-100
    public const byte Sharpness        = 0x8A; // RW, 0-100
    public const byte RedGain          = 0x8C; // RW, 0-255 (some monitors 25-200)
    public const byte GreenGain        = 0x8E; // RW
    public const byte BlueGain         = 0x90; // RW
    public const byte ColorTemperature = 0xCC; // RW, 0x01=preset, 0x0B=custom 1000-10000K (driver-dependent)
    public const byte InputSource      = 0x60; // RW, 0x11=HDMI1, 0x12=HDMI2, 0x1A=DP1, 0x1B=DP2, 0x1E=USB-C (varies, use Capabilities string)
    public const byte Volume           = 0x62; // RW, speaker volume 0-100 (if present)
    public const byte Power            = 0xD6; // RW, 0x01=on, 0x04=standby, 0x05=off
    public const byte FactoryReset     = 0x04; // W, 0x01=factory reset
    public const byte OSD              = 0x0A; // RW, 0x00=hide, 0x01=show
    public const byte Language         = 0x5E; // RW
    public const byte Mute             = 0x8D; // RW, 0x00=off, 0x01=mute
    public const byte PowerSave        = 0xD2; // RW

    /// <summary>Standard input-source values (see MCCS). Use GetCapabilities to verify per monitor.</summary>
    public static class Input
    {
        public const uint Vga    = 0x01;
        public const uint Dvi1   = 0x03;
        public const uint Dvi2   = 0x04;
        public const uint Hdmi1  = 0x11;
        public const uint Hdmi2  = 0x12;
        public const uint Hdmi3  = 0x13;
        public const uint Dp1    = 0x1A;
        public const uint Dp2    = 0x1B;
        public const uint UsbC   = 0x1E;
    }

    public static class PowerMode
    {
        public const uint On      = 0x01;
        public const uint Standby = 0x04;
        public const uint Off     = 0x05;
    }
}
