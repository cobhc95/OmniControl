namespace OmniControl.Core.Native;

/// gdi32 gamma ramps (per display device) and user32 display queries for them.
internal static class GammaNative
{
    /// A ramp is 256 red, then 256 green, then 256 blue entries of 16 bits.
    public const int RampLength = 3 * 256;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] ramp);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);
}
