using System.ComponentModel;
using System.Runtime.InteropServices;
using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// Software brightness, contrast and colour temperature for one display, by setting its gamma ramp. Before the first change the
/// display's own ramp is captured (<see cref="Capture"/>) so <see cref="Restore"/> can put exactly that back; a caller that must
/// survive a crash saves <see cref="Original"/> first and writes it back with <see cref="Write"/> at the next start.
/// </summary>
public sealed class GammaDisplay
{
    public string DeviceName { get; }

    /// <summary>The display's ramp as it was before OmniControl changed it; null until captured.</summary>
    public ushort[]? Original { get; private set; }

    public GammaDisplay(string deviceName) => DeviceName = deviceName;

    /// <summary>Reads and keeps the display's current ramp (once). Fails when Windows will not give it.</summary>
    public OperationResult Capture()
    {
        if (Original is not null) return OperationResult.Ok();
        var ramp = Read(DeviceName);
        if (ramp is null) return OperationResult.Fail("Windows would not let OmniControl read this display's colour curve.");
        Original = RampMath.IsSane(ramp) ? ramp : RampMath.Identity(); // a driver answering with zeros must not become "normal"
        return OperationResult.Ok();
    }

    /// <summary>Applies <paramref name="color"/> (its values limited to the safe ranges). Capture first; neutral puts the original back.</summary>
    public OperationResult Apply(SoftwareColor color)
    {
        if (Original is null) return OperationResult.Fail("The display's own colour curve was not captured.");
        if (color.IsNeutral) return Write(DeviceName, Original);
        var result = Write(DeviceName, RampMath.Build(color, Original));
        return result.Success ? result : OperationResult.Fail("Windows refused this setting for this display. Try a value closer to normal.");
    }

    /// <summary>Puts the display's own ramp back and forgets it.</summary>
    public OperationResult Restore()
    {
        if (Original is null) return OperationResult.Ok();
        var result = Write(DeviceName, Original);
        Original = null;
        return result;
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
