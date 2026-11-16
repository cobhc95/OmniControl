using Avalonia.Media;

namespace OmniControl.App.Services;

/// <summary>Colours that code and styles both need, defined once. Everything else comes from theme resources and <see cref="AccentService"/>.</summary>
public static class Palette
{
    public const string DangerHex = "#E5534B", DarkBackgroundHex = "#161616", CharcoalHex = "#202020";

    /// <summary>Muted or clipping state (OSD at zero, meter clip mark) and the red accent choice; the "risk" tag colour.</summary>
    public static readonly Color Danger = Color.Parse(DangerHex);

    /// <summary>The "on" green (an FX chain switched on) and the "good" tag colour (theme brush <c>BrushOn</c>).</summary>
    public static readonly Color On = Color.Parse("#3FB950");

    /// <summary>The caution yellow (a "caution" tag, a setup that is missing); <see cref="CautionInk"/> is its darker form for text on the light theme.</summary>
    public static readonly Color Caution = Color.Parse("#D29922");
    public static readonly Color CautionInk = Color.Parse("#9A6700");

    /// <summary>The cable blue (a Mixer row's Method chip when the sound goes through a cable or the cable bus: BrushCable); <see cref="CableInk"/> is its darker form for text on the light theme.</summary>
    public static readonly Color Cable = Color.Parse("#4A9BE6");
    public static readonly Color CableInk = Color.Parse("#1F6FB8");

    /// <summary>The OSD's grey accent.</summary>
    public static readonly Color OsdGrey = Color.Parse("#9A9A9A");

    /// <summary>The screenshot overlay's drawing colours (its colour pop-down), the first one the default: picture content, the same in both themes.</summary>
    public static readonly IReadOnlyList<Color> Drawing = new[]
    {
        Danger, Color.Parse("#F28C28"), Color.Parse("#F2CC0C"), On, Cable, Color.Parse("#A371F7"), Colors.Black, Colors.White,
    };

    /// <summary>The accent's soft glow around a lit button (AccentGlow, made by <see cref="AccentService"/>): its strength, blur and spread in pixels.</summary>
    public const double GlowOpacity = 0.55, GlowBlur = 8, GlowSpread = 1;

    /// <summary>Opacity of a smart device's icon colour behind its glyph while the device is on (Pages/Devices/DeviceIconView).</summary>
    public const double IconWashOpacity = 0.25;

    /// <summary>Opacity of the see-through colour wash behind a warning (BrushCautionSoft).</summary>
    public const double WashOpacity = 0.16;

    /// <summary>How faint a tag is: the opacity of its coloured edge and of its wash (BrushTagGood/Caution/Risk and their Soft washes), so tags stay quieter than the full-strength colours.</summary>
    public const double TagEdgeOpacity = 0.5, TagWashOpacity = 0.08;

    /// <summary>How much accent a calm accent edge carries (BrushAccentEdge: the Selected tag and a selected pop-down card), blended into the page background.</summary>
    public const double AccentEdgeStrength = 0.6;

    public static readonly Color DarkBackground = Color.Parse(DarkBackgroundHex);

    /// <summary>Dark text on a light background (the OSD on its light background choice).</summary>
    public static readonly Color Ink = Color.Parse("#1A1A1A");

    /// <summary>The OSD's light choice, both as an accent ("White") and as a background ("Light").</summary>
    public const string LightHex = "#F2F2F2";

    /// <summary>A level meter's bar below the warm level and above it (TabForge's track meter colours; slightly different from <see cref="On"/> on purpose).</summary>
    public static readonly Color MeterGreen = Color.Parse("#3FB36B"), MeterAmber = Color.Parse("#E8B04A");
}
