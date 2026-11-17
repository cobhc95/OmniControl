using Avalonia;
using Avalonia.Platform;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Osd;

/// <summary>
/// The OSD's one view of the screens: how a screen is told apart in the settings, what it is called, and which screens a pop-up goes to
/// for a "Show on" choice. Pure apart from asking Core where the mouse is.
/// </summary>
public static class OsdScreens
{
    /// <summary>What the settings remember a screen by: Windows' name for it, else where it sits (a screen without a name).</summary>
    public static string KeyOf(Screen screen) =>
        string.IsNullOrWhiteSpace(screen.DisplayName) ? $"{screen.Bounds.X},{screen.Bounds.Y}" : screen.DisplayName;

    /// <summary>"Screen 2 · 2560 x 1440" (the primary one says so): Windows' order, the numbers the Displays page's screens have.</summary>
    public static string NameOf(IReadOnlyList<Screen> all, Screen screen)
    {
        var number = all.ToList().IndexOf(screen) + 1;
        return $"Screen {number} · {screen.Bounds.Width} x {screen.Bounds.Height}{(screen.IsPrimary ? " (primary)" : string.Empty)}";
    }

    /// <summary>What the "Show on" chip says for the settings now.</summary>
    public static string Describe(IReadOnlyList<Screen> all, OsdSettings settings)
    {
        if (settings.ShowOn == OsdShowOn.MouseScreen) return "Screen with the mouse";
        var chosen = settings.ShowOn == OsdShowOn.Chosen ? all.Where(s => settings.Screens.Contains(KeyOf(s))).ToList() : new List<Screen>();
        return chosen.Count switch { 0 => "All screens", 1 => NameOf(all, chosen[0]), _ => $"{chosen.Count} screens" };
    }

    /// <summary>The screens a pop-up is shown on now; never none (a chosen screen that is no longer there falls back to all).</summary>
    public static IReadOnlyList<Screen> Targets(IReadOnlyList<Screen> all, OsdSettings settings)
    {
        if (all.Count <= 1) return all;
        if (settings.ShowOn == OsdShowOn.MouseScreen && UnderMouse(all) is { } hovered) return new[] { hovered };
        if (settings.ShowOn == OsdShowOn.Chosen && all.Where(s => settings.Screens.Contains(KeyOf(s))).ToList() is { Count: > 0 } chosen) return chosen;
        return all;
    }

    private static Screen? UnderMouse(IReadOnlyList<Screen> all)
    {
        var monitor = ScreenCapture.MonitorUnderPointer();
        var centre = new PixelPoint(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2);
        return all.FirstOrDefault(s => s.Bounds.Contains(centre));
    }
}
