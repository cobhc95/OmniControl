namespace OmniControl.App.Shared.Services;

/// Colours that code and styles both need, defined once. Everything else comes from theme resources and <see cref="AccentService"/>.
public static class Palette
{
    public const string DangerHex = "#E5534B", CharcoalHex = "#202020";

    /// Muted or clipping state (OSD at zero, meter clip mark) and the red accent choice; the "risk" tag colour.
    public static readonly Color Danger = Color.Parse(DangerHex);

    /// The "on" green (an FX chain switched on) and the "good" tag colour (theme brush <c>BrushOn</c>).
    public static readonly Color On = Color.Parse("#3FB950");

    /// The caution yellow (a "caution" tag, a setup that is missing); <see cref="CautionInk"/> is its darker form for text on the light theme.
    public static readonly Color Caution = Color.Parse("#D29922");
    public static readonly Color CautionInk = Color.Parse("#9A6700");

    /// The cable blue (a Mixer row's Method chip when the sound goes through a cable or the cable bus: BrushCable); <see cref="CableInk"/> is its darker form for text on the light theme.
    public static readonly Color Cable = Color.Parse("#4A9BE6");
    public static readonly Color CableInk = Color.Parse("#1F6FB8");

    /// The OSD's grey accent.
    public static readonly Color OsdGrey = Color.Parse("#9A9A9A");

    /// The drawing colours with the names a saved text may call them by (a gadget label's coloured icon, "{upload:green}").
    public static readonly IReadOnlyList<(string Name, Color Colour)> DrawingNamed = new[]
    {
        ("red", Danger), ("orange", Color.Parse("#F28C28")), ("yellow", Color.Parse("#F2CC0C")), ("green", On), ("blue", Cable), ("purple", Color.Parse("#A371F7")),
        ("black", Colors.Black), ("white", Colors.White),
    };

    /// The screenshot overlay's drawing colours (its colour pop-down), the first one the default: picture content, the same in both themes.
    public static readonly IReadOnlyList<Color> Drawing = DrawingNamed.Select(d => d.Colour).ToArray();

    /// The accent's soft glow around a lit button (AccentGlow, made by <see cref="AccentService"/>): its strength, blur and spread in pixels.
    public const double GlowOpacity = 0.55, GlowBlur = 8, GlowSpread = 1;

    /// Opacity of a smart device's icon colour behind its glyph while the device is on (Pages/Devices/DeviceIconView).
    public const double IconWashOpacity = 0.25;

    /// Opacity of the see-through colour wash behind a warning (BrushCautionSoft).
    public const double WashOpacity = 0.16;

    /// How faint a tag is: the opacity of its coloured edge and of its wash (BrushTagGood/Caution/Risk and their Soft washes), so tags stay quieter than the full-strength colours.
    public const double TagEdgeOpacity = 0.5, TagWashOpacity = 0.08;

    /// How much accent a calm accent edge carries (BrushAccentEdge: the Selected tag and a selected pop-down card), blended into the page background.
    public const double AccentEdgeStrength = 0.6;

    /// The near-black ink of words and glyphs on a light accent fill (Windows' own: black at about 90 %), chosen by <see cref="AccentService.OnAccent"/>.
    public static readonly Color OnAccentInk = Color.FromArgb(0xE6, 0, 0, 0);

    /// How Windows shades an accent for painting (<see cref="ColorMath.AccentShade"/>): the share of the way to white its HSL lightness moves on
    /// the dark theme (Accent Light 2), and the share it loses on the light theme (Accent Dark 1); matched to Windows' blue #0078D4.
    public const double AccentLightenDark = 0.4, AccentDarkenLight = 0.25;

    /// The light theme darkens an accent until its luminance (<see cref="ColorMath.Luminance"/>) is at most this, so a very light accent (White)
    /// still shows as an edge on the white page; the usual accents are already below it after the light theme's own shade.
    public const double LightAccentMaxLuminance = 0.56;

    /// Dark text on a light background (the OSD on its light background choice).
    public static readonly Color Ink = Color.Parse("#1A1A1A");

    /// The light choice: the "White" accent (the program's and the OSD's) and the OSD's "Light" background.
    public const string LightHex = "#F2F2F2";

    /// A level meter's bar below the warm level and above it (TabForge's track meter colours; slightly different from <see cref="On"/> on purpose).
    public static readonly Color MeterGreen = Color.Parse("#3FB36B"), MeterAmber = Color.Parse("#E8B04A");
}
