using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>Where on the screen the OSD appears. Display names are shown in the settings page.</summary>
public enum OsdPosition { TopLeft, TopCenter, TopRight, Center, BottomLeft, BottomCenter, BottomRight }

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
