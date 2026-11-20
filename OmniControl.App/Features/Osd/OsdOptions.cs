namespace OmniControl.App.Features.Osd;

/// Where on the screen the OSD appears. Display names are shown in the settings page.
public enum OsdPosition { TopLeft, TopCenter, TopRight, Center, BottomLeft, BottomCenter, BottomRight }

/// The nine points of a screen a place can be pinned to (the placement window's 3 x 3 grid).
public enum OsdAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

/// Which screens show the pop-up: all of them, the one the mouse is on, or the ones chosen (<see cref="OsdSettings.Screens"/>).
public enum OsdShowOn { AllScreens, MouseScreen, Chosen }

/// Where the pop-up is on a screen: pinned to an <see cref="OsdAnchor"/> and moved from it by two offsets in device-independent pixels, so a
/// place survives a change of resolution. <paramref name="Across"/> is the sideways move (inward from a left or right anchor, to the right
/// from a middle one); <paramref name="Away"/> the up-down move (down from a top anchor, up from a bottom or middle one).
public readonly record struct OsdSpot(OsdAnchor Anchor, int Across, int Away);

/// A place the user named (an entry of the OSD place presets), kept in osd.json.
public sealed class OsdPlace
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public OsdAnchor Anchor { get; set; } = OsdAnchor.Bottom;
    public int Across { get; set; }
    public int Away { get; set; }

    public OsdSpot ToSpot() => new(Anchor, Across, Away);

    public static OsdPlace Of(string id, string name, OsdSpot spot) =>
        new() { Id = id, Name = name, Anchor = spot.Anchor, Across = spot.Across, Away = spot.Away };
}

/// The look of the pop-up.
public enum OsdStyle { Classic, Pill, BigNumber, Ring, Vertical }

/// Named colour choices for the OSD, browsed in the settings page.
public static class OsdPalette
{
    /// The program's background colour choices (their dark page tone, <see cref="SurfaceService"/>), then Light; a saved colour not on the list still shows.
    public static readonly IReadOnlyList<(string Name, string Hex)> Backgrounds = SurfaceService.Names
        .Where(n => n != SurfaceService.FollowName)
        .Select(n => (n, ColorMath.Hex(SurfaceService.Swatch(n))))
        .Append(("Light", Palette.LightHex))
        .ToList();
}
