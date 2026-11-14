using System.ComponentModel;
using System.Runtime.InteropServices;
using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>One display's gamma ramp as the software picture needs it; <see cref="GammaDisplay"/> is the Windows one, a test has its own.</summary>
public interface IGammaDisplay
{
    /// <summary>The display's ramp as it was before OmniControl changed it; null until captured.</summary>
    ushort[]? Original { get; }

    /// <summary>True once the driver has shown it does not give or take ramps from programs (its own ramp could not be read, or the last good one written again was refused too).</summary>
    bool DriverRefuses { get; }

    OperationResult Capture();
    OperationResult Apply(SoftwareColor color);
}

/// <summary>
/// The software picture of one display (brightness, contrast, gamma, black and white level, channel gains, colour temperature) by
/// setting its gamma ramp. Before the first change the display's own ramp is captured (<see cref="Capture"/>) so a neutral
/// <see cref="Apply"/> can put exactly that back; a caller that must survive a crash saves <see cref="Original"/> first and writes it back with
/// <see cref="Write"/> at the next start.
/// </summary>
public sealed class GammaDisplay : IGammaDisplay
{
    /// <summary>What a driver that takes no ramps from programs is called, in a card's tooltip.</summary>
    public const string DriverRefusal = "Not supported by this display's driver";

    private ushort[]? _lastGood;

    public string DeviceName { get; }

    public ushort[]? Original { get; private set; }

    public bool DriverRefuses { get; private set; }

    public GammaDisplay(string deviceName) => DeviceName = deviceName;

    /// <summary>Reads and keeps the display's current ramp (once). Fails when Windows will not give it.</summary>
    public OperationResult Capture()
    {
        if (Original is not null) return OperationResult.Ok();
        var ramp = Read(DeviceName);
        if (ramp is null)
        {
            DriverRefuses = true;
            return OperationResult.Fail("Windows would not let OmniControl read this display's colour curve.");
        }
        DriverRefuses = false;
        Original = RampMath.IsSane(ramp) ? ramp : RampMath.Identity(); // a driver answering with zeros must not become "normal"
        _lastGood = Original;
        return OperationResult.Ok();
    }

    /// <summary>
    /// Applies <paramref name="color"/> (its values limited to the safe ranges). Capture first; neutral puts the original back. When
    /// Windows refuses a ramp, the ramp that worked last is written again: if even that is refused the driver takes no ramps at all
    /// (<see cref="DriverRefuses"/>), otherwise only this value was too far from normal.
    /// </summary>
    public OperationResult Apply(SoftwareColor color)
    {
        if (Original is null) return OperationResult.Fail("The display's own colour curve was not captured.");
        var ramp = color.IsNeutral ? Original : RampMath.Build(color, Original);
        var result = Write(DeviceName, ramp);
        if (result.Success)
        {
            DriverRefuses = false;
            _lastGood = ramp;
            return result;
        }
        if (color.IsNeutral) return result;

        DriverRefuses = !Write(DeviceName, _lastGood!).Success;
        return OperationResult.Fail(DriverRefuses ? DriverRefusal : "Windows refused this setting for this display. Try a value closer to normal.");
    }

    /// <summary>Sets a ramp on a display by its device name.</summary>
    public static OperationResult Write(string deviceName, ushort[] ramp)
    {
        if (ramp.Length != GammaNative.RampLength) return OperationResult.Fail("That is not a colour curve.");
        var dc = GammaNative.CreateDC(null, deviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return OperationResult.Fail($"Display {deviceName} is not available.");
        try
        {
            return GammaNative.SetDeviceGammaRamp(dc, ramp)
                ? OperationResult.Ok()
                : OperationResult.Fail($"SetDeviceGammaRamp failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
        finally { GammaNative.DeleteDC(dc); }
    }

    /// <summary>The ramp a display has now, or null when Windows will not say.</summary>
    public static ushort[]? Read(string deviceName)
    {
        var dc = GammaNative.CreateDC(null, deviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            var ramp = new ushort[GammaNative.RampLength];
            return GammaNative.GetDeviceGammaRamp(dc, ramp) ? ramp : null;
        }
        finally { GammaNative.DeleteDC(dc); }
    }
}
