using OmniDial.Core.Models;
#if WINDOWS
using OmniDial.Core.Native;
using System.ComponentModel;
#endif

namespace OmniDial.Core.Engine;

public enum DisplayRotation { Landscape = 0, Portrait = 1, LandscapeFlipped = 2, PortraitFlipped = 3 }

/// <summary>Display rotation — ChangeDisplaySettingsEx on Windows, xrandr stub on Linux.</summary>
public static class DisplayRotationController
{
#if WINDOWS
    public static OperationResult SetRotation(string deviceName, DisplayRotation rot)
    {
        var dm = new Dxva2Native.DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Dxva2Native.DEVMODE)) };
        if (!Dxva2Native.EnumDisplaySettings(deviceName, Dxva2Native.ENUM_CURRENT_SETTINGS, ref dm))
            return OperationResult.Fail($"EnumDisplaySettings {deviceName} failed: {new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message}");

        bool swap = rot is DisplayRotation.Portrait or DisplayRotation.PortraitFlipped;
        if (swap)
        {
            (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
        }
        dm.dmDisplayOrientation = (uint)rot;
        dm.dmFields = 0x00000000;

        var copy = dm;
        int rc = Dxva2Native.ChangeDisplaySettingsEx(deviceName, in copy, IntPtr.Zero,
            Dxva2Native.CDS_NORESET | Dxva2Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (rc != 0 && rc != 1)
            return OperationResult.Fail($"ChangeDisplaySettingsEx returned {rc} (0=success,1=restart).");

        rc = Dxva2Native.ChangeDisplaySettingsEx(null, in copy, IntPtr.Zero, 0, IntPtr.Zero);
        return OperationResult.Ok(rc == 0 ? "Applied" : $"Apply code {rc} (1=restart required)");
    }

    public static OperationResult GetRotation(string deviceName, out DisplayRotation rot)
    {
        rot = DisplayRotation.Landscape;
        var dm = new Dxva2Native.DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Dxva2Native.DEVMODE)) };
        if (!Dxva2Native.EnumDisplaySettings(deviceName, Dxva2Native.ENUM_CURRENT_SETTINGS, ref dm))
            return OperationResult.Fail("EnumDisplaySettings failed.");
        rot = (DisplayRotation)dm.dmDisplayOrientation;
        return OperationResult.Ok(rot.ToString());
    }
#else
    public static OperationResult SetRotation(string deviceName, DisplayRotation rot) =>
        OperationResult.Fail("Linux rotation: implement via xrandr --output <out> --rotate <normal|left|inverted|right>.");
    public static OperationResult GetRotation(string deviceName, out DisplayRotation rot)
    {
        rot = DisplayRotation.Landscape;
        return OperationResult.Fail("Linux rotation query not implemented.");
    }
#endif
}
