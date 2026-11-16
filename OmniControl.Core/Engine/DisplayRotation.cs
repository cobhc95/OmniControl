using System.Runtime.InteropServices;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// Reads and sets a display's orientation through Windows (DEVMODE <c>dmDisplayOrientation</c>), which works for any display, the built-in
/// one included. Going between landscape and portrait swaps the mode's width and height. The values follow Windows' own: DMDO_270 is what
/// Settings calls Portrait and DMDO_90 is Portrait (flipped).
/// </summary>
public sealed class DisplayRotation : IDisplayRotation
{
    private static readonly uint[] DmdoOf = { 0, 3, 2, 1 }; // by DisplayOrientation: Landscape, Portrait, LandscapeFlipped, PortraitFlipped

    public DisplayOrientation? Read(string deviceName)
    {
        var mode = NewMode();
        if (!DisplaySettingsNative.EnumDisplaySettingsEx(deviceName, DisplaySettingsNative.CurrentSettings, ref mode, 0)) return null;
        var index = Array.IndexOf(DmdoOf, mode.dmDisplayOrientation);
        return index < 0 ? null : (DisplayOrientation)index;
    }

    public OperationResult Set(string deviceName, DisplayOrientation orientation)
    {
        var mode = NewMode();
        if (!DisplaySettingsNative.EnumDisplaySettingsEx(deviceName, DisplaySettingsNative.CurrentSettings, ref mode, 0))
            return OperationResult.Fail($"Windows would not say how {deviceName} is set now (error {Marshal.GetLastWin32Error()}).");

        var target = DmdoOf[(int)orientation];
        if (mode.dmDisplayOrientation == target) return OperationResult.Ok();

        // 90 and 270 degrees are the portrait ones, so the two sides trade places when the odd/even kind changes.
        if ((mode.dmDisplayOrientation + target) % 2 == 1)
            (mode.dmPelsWidth, mode.dmPelsHeight) = (mode.dmPelsHeight, mode.dmPelsWidth);
        mode.dmDisplayOrientation = target;
        mode.dmFields |= DisplaySettingsNative.FieldOrientation | DisplaySettingsNative.FieldWidth | DisplaySettingsNative.FieldHeight;

        var code = DisplaySettingsNative.ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, DisplaySettingsNative.UpdateRegistry, IntPtr.Zero);
        return code == DisplaySettingsNative.Successful ? OperationResult.Ok() : OperationResult.Fail($"Windows refused ({Describe(code)}).");
    }

    private static DisplaySettingsNative.DEVMODE NewMode() =>
        new() { dmDeviceName = string.Empty, dmFormName = string.Empty, dmSize = (ushort)Marshal.SizeOf<DisplaySettingsNative.DEVMODE>() };

    private static string Describe(int code) => code switch
    {
        -1 => "DISP_CHANGE_FAILED",
        -2 => "mode not supported",
        -3 => "settings could not be written",
        -4 => "bad flags",
        -5 => "bad parameter",
        -6 => "bad dual-view setup",
        1 => "restart needed",
        _ => $"code {code}",
    };
}
