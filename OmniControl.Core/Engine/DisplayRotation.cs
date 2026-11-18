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
        return FromDmdo(mode.dmDisplayOrientation);
    }

    public OperationResult Set(string deviceName, DisplayOrientation orientation)
    {
        var mode = NewMode();
        if (!DisplaySettingsNative.EnumDisplaySettingsEx(deviceName, DisplaySettingsNative.CurrentSettings, ref mode, 0))
            return OperationResult.Fail($"Windows would not say how {deviceName} is set now (error {Marshal.GetLastWin32Error()}).");

        var target = ToDmdo(orientation);
        if (mode.dmDisplayOrientation == target) return Remember();

        // 90 and 270 degrees are the portrait ones, so the two sides trade places when the odd/even kind changes.
        if (SwapsSides(mode.dmDisplayOrientation, target))
            (mode.dmPelsWidth, mode.dmPelsHeight) = (mode.dmPelsHeight, mode.dmPelsWidth);
        mode.dmDisplayOrientation = target;
        mode.dmFields |= DisplaySettingsNative.FieldPosition | DisplaySettingsNative.FieldOrientation |DisplaySettingsNative.FieldWidth | DisplaySettingsNative.FieldHeight;

        var code = DisplaySettingsNative.ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, DisplaySettingsNative.UpdateRegistry, IntPtr.Zero);
        if (code != DisplaySettingsNative.Successful) return OperationResult.Fail($"Windows refused ({Describe(code)}).");

        return Remember();
    }

    // The legacy registry write alone is not what Windows restores from after sleep: its display database (the one Settings
    // saves to) must hold the turn too, or the display can come back turned the other way. Also done when the display is already
    // turned as asked, so a stale stored turn is corrected.
    private static OperationResult Remember()
    {
        var saved = DisplayConfigNative.SaveCurrentToDatabase();
        return saved == 0 ? OperationResult.Ok() : OperationResult.Fail($"The display turned but Windows would not remember it (error {saved}).");
    }

    /// <summary>The DEVMODE value (DMDO_*) for an orientation.</summary>
    internal static uint ToDmdo(DisplayOrientation orientation) => DmdoOf[(int)orientation];

    /// <summary>The orientation for a DEVMODE value; null for a value Windows does not define.</summary>
    internal static DisplayOrientation? FromDmdo(uint dmdo)
    {
        var index = Array.IndexOf(DmdoOf, dmdo);
        return index < 0 ? null : (DisplayOrientation)index;
    }

    /// <summary>Whether going between two DMDO values trades the mode's width and height (landscape to portrait or back).</summary>
    internal static bool SwapsSides(uint from, uint to) => (from + to) % 2 == 1;

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
