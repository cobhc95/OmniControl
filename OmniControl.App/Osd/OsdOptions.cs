using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>Where on the screen the OSD appears. Display names are shown in the settings page.</summary>
public enum OsdPosition { TopLeft, TopCenter, TopRight, Center, BottomLeft, BottomCenter, BottomRight }

/// <summary>The nine points of a screen a place can be pinned to (the placement window's 3 x 3 grid).</summary>
public enum OsdAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

/// <summary>Which screens show the pop-up: all of them, the one the mouse is on, or the ones chosen (<see cref="OsdSettings.Screens"/>).</summary>
public enum OsdShowOn { AllScreens, MouseScreen, Chosen }

/// <summary>
/// Where the pop-up is on a screen: pinned to an <see cref="OsdAnchor"/> and moved from it by two offsets in device-independent pixels, so a
/// place survives a change of resolution. <paramref name="Across"/> is the sideways move (inward from a left or right anchor, to the right
/// from a middle one); <paramref name="Away"/> the up-down move (down from a top anchor, up from a bottom or middle one).
/// </summary>
public readonly record struct OsdSpot(OsdAnchor Anchor, int Across, int Away);

/// <summary>A place the user named (an entry of the OSD place presets), kept in osd.json.</summary>
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

/// <summary>The look of the pop-up.</summary>
public enum OsdStyle { Classic, Pill, BigNumber, Ring, Vertical }

/// <summary>Named colour choices for the OSD, browsed in the settings page.</summary>
public static class OsdPalette
{
    /// <summary>Extra OSD-only accents (not offered for the whole program: white would make text on accent buttons unreadable).</summary>
    public static readonly IReadOnlyList<(string Name, Color Color)> ExtraAccents = new[]
    {
        ("White", Color.Parse(Palette.LightHex)), ("Grey", Palette.OsdGrey),
    };

    public static readonly IReadOnlyList<(string Name, string Hex)> Backgrounds = new[]
    {
        ("Charcoal", Palette.CharcoalHex), ("Black", "#0B0B0B"), ("Slate", "#1F2933"), ("Navy", "#14213D"),
        ("Plum", "#2A1B33"), ("Forest", "#16251C"), ("Light", Palette.LightHex),
    };
}
