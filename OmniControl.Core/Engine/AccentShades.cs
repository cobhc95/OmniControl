using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace OmniControl.Core.Engine;

/// The shades of Windows' accent that Windows itself paints controls with: "Accent Light 2" on the dark theme and "Accent Dark 1" on the
/// light theme (UISettings; Avalonia's platform settings give only the raw accent). Each as 0xAARRGGBB; null when this Windows cannot say.
public static class AccentShades
{
    public static (uint ForDark, uint ForLight)? Read()
    {
        try
        {
            var ui = new UISettings();
            return (Pack(ui.GetColorValue(UIColorType.AccentLight2)), Pack(ui.GetColorValue(UIColorType.AccentDark1)));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException or PlatformNotSupportedException)
        {
            return null; // older Windows without UISettings colours: the caller derives the shades itself
        }
    }

    private static uint Pack(Windows.UI.Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
}
