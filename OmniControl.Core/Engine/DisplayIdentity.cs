using OmniControl.Core.Models;

namespace OmniControl.Core.Engine;

/// <summary>
/// Finds a display again by what stays the same when Windows renumbers them (a replug, a driver reset, a GPU switch can turn
/// <c>\.\DISPLAY2</c> into <c>\.\DISPLAY3</c>): the monitor's device path. A display is never matched by its GDI name alone when it has a
/// path, so a turn meant for one screen can never land on another.
/// </summary>
public static class DisplayIdentity
{
    /// <summary>The GDI name the remembered display has in <paramref name="now"/>; null when it is not attached.</summary>
    public static string? Resolve(DisplayInfo remembered, IReadOnlyList<DisplayInfo> now)
    {
        if (remembered.DevicePath.Length > 0)
            return now.FirstOrDefault(d => string.Equals(d.DevicePath, remembered.DevicePath, StringComparison.OrdinalIgnoreCase))?.DeviceName;

        // Windows gave no path: the same name and the same monitor name together are the best that is left.
        return now.FirstOrDefault(d => d.DevicePath.Length == 0 && string.Equals(d.DeviceName, remembered.DeviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.Name, remembered.Name, StringComparison.Ordinal))?.DeviceName;
    }
}
