using Avalonia.Platform;
using Avalonia.Styling;

namespace OmniControl.App.Shared.Services;

/// Colour helpers shared by the accent service and the OSD.
public static class ColorMath
{
    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// WCAG relative luminance (linear light), the measure readable-text contrast is judged by.
    public static double RelativeLuminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// The shade Windows paints controls with for an accent: lighter on the dark theme, darker on the light theme (HSL lightness moved
    /// <see cref="Palette.AccentLightenDark"/> of the way to white, or by <see cref="Palette.AccentDarkenLight"/> towards black), both times
    /// <paramref name="scale"/> (1 = Windows' own amount, 0 = the accent as it is).
    public static Color AccentShade(Color accent, bool darkTheme, double scale = 1)
    {
        var hsl = accent.ToHsl();
        var lightness = darkTheme
            ? hsl.L + (1 - hsl.L) * Math.Min(1, Palette.AccentLightenDark * scale)
            : hsl.L * (1 - Math.Min(1, Palette.AccentDarkenLight * scale));
        return new HslColor(hsl.A, hsl.H, hsl.S, lightness).ToRgb();
    }

    /// "#RRGGBB" of an opaque colour (how a custom accent is typed and saved).
    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// The opaque colour of "#RRGGBB" or "RRGGBB" (spaces ignored); null when it is not one.
    public static Color? ParseHex(string? text)
    {
        var t = text?.Trim().TrimStart('#');
        if (t is not { Length: 6 } || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return null;
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// WCAG contrast ratio of two opaque colours (1 to 21).
    public static double Contrast(Color a, Color b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Linear(byte channel)
    {
        var s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}

/// The one place the program's accent colour lives. Pick a choice with <see cref="Set"/>; every accent brush the styles use
/// (BrushAccent, BrushAccentSoft, BrushAccentHover, BrushAccentEdge, BrushAccentBright, the toggle-switch fills, and BrushOnAccent, the words
/// or knob drawn ON an accent fill) is derived for both themes from the shade Windows paints with (Accent Light 2 on the dark theme, Accent
/// Dark 1 on the light one: Windows' own for "Windows accent", <see cref="ColorMath.AccentShade"/> for the others), and updates live.
/// "Windows accent" follows Windows' own accent colour (read through Avalonia's platform settings, which hear Windows' colour change notice);
/// when Windows reports none, it is Blue. "Custom" is the user's own colour (<see cref="Custom"/>, made in App settings' colour editor), kept
/// while another choice is in use. <see cref="Intensity"/> scales the shade for every choice: at <see cref="WindowsIntensity"/> it is Windows'
/// own amount (and Windows' own shades for "Windows accent"); the default is stronger. Nothing else in the program hard-codes an accent colour: use <see cref="Current"/>
/// (the colour as picked) in code or the DynamicResource brushes in XAML.
public static class AccentService
{
    /// The choice that follows Windows' accent colour; first in <see cref="Names"/>.
    public const string WindowsName = "Windows accent";

    /// The user's own colour; last in <see cref="Choices"/>. <see cref="CustomEditorLabel"/> = the words of the row that opens its colour editor.
    public const string CustomName = "Custom", CustomEditorLabel = "Custom colour…";

    /// The choice "Windows accent" falls back to when Windows reports no accent.
    private const string FallbackName = "Blue";

    /// Windows' own accent palette (its saturated base colours, which it shades for painting as it does its own accent); the names are the
    /// saved ids, so older settings files keep their choice. White is the program's own light choice.
    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new[]
    {
        ("Orange", "#F7630C"), ("Blue", "#0078D4"), ("Green", "#10893E"), ("Pink", "#E3008C"),
        ("Purple", "#8764B8"), ("Red", "#E81123"), ("Yellow", "#FFB900"), ("Teal", "#038387"), ("White", Palette.LightHex),
    };

    /// Choices only the OSD offers after <see cref="Names"/>; resolved and shaded like the presets.
    public static readonly IReadOnlyList<(string Name, string Hex)> OsdExtras = new[] { ("Grey", Palette.GreyHex) };

    private static readonly (string Name, string Hex)[] AllPresets = Presets.Concat(OsdExtras).ToArray();
    private static readonly Color[] PresetColours = AllPresets.Select(p => Color.Parse(p.Hex)).ToArray();

    /// The custom colour before the user has made one: the first preset.
    public static string DefaultCustomHex => Presets[0].Hex;

    /// Intensity: 0 = softest (twice Windows' shade), <see cref="WindowsIntensity"/> = Windows' own shade, <see cref="MaxIntensity"/> = the colour
    /// unshaded; <see cref="DefaultIntensity"/> is close to the raw colour, as Windows' toggles look.
    public const int WindowsIntensity = 50, DefaultIntensity = 75, MaxIntensity = 100;

    /// Windows accent, then the fixed colours (the galleries that offer no custom colour, such as the OSD's).
    public static IReadOnlyList<string> Names { get; } = Presets.Select(p => p.Name).Prepend(WindowsName).ToList();

    /// Every choice in the order App settings' gallery shows it: <see cref="Names"/>, then <see cref="CustomName"/>.
    public static IReadOnlyList<string> Choices { get; } = Names.Append(CustomName).ToList();

    private static readonly ThemeBrushes Written = new();

    public static string Name { get; private set; } = WindowsName;

    /// The user's own colour, kept while another choice is in use.
    public static Color Custom { get; private set; } = Color.Parse(DefaultCustomHex);

    /// How strongly every accent choice is shaded (see <see cref="DefaultIntensity"/>).
    public static int Intensity { get; private set; } = DefaultIntensity;

    /// The chosen accent as picked (before the theme's shade).
    public static Color Current => Resolve(Name);

    public static event Action? Changed;

    /// The colour of a choice as picked. Asked on every OSD step, so it allocates nothing.
    public static Color Resolve(string name)
    {
        if (name == WindowsName) return WindowsAccent();
        if (name == CustomName) return Custom;
        for (var i = 0; i < AllPresets.Length; i++) if (AllPresets[i].Name == name) return PresetColours[i];
        return PresetColours[0];
    }

    /// The intensity in words: "Windows" at the default, else which way and how far ("Softer 30", "Stronger 70").
    public static string IntensityWords(int value) => value == WindowsIntensity ? "Windows" : $"{(value < WindowsIntensity ? "Softer" : "Stronger")} {value}";

    /// The words or glyph drawn on a fill of <paramref name="fill"/>, as Windows does: near-black ink (<see cref="Palette.OnAccentInk"/>) or
    /// white, whichever reads better on it (so dark ink on the dark theme's light shades, white on the light theme's darker ones).
    public static Color OnAccent(Color fill)
    {
        var ink = ColorMath.Mix(fill, Color.FromRgb(Palette.OnAccentInk.R, Palette.OnAccentInk.G, Palette.OnAccentInk.B), Palette.OnAccentInk.A / 255.0);
        return ColorMath.Contrast(ink, fill) >= ColorMath.Contrast(Colors.White, fill) ? Palette.OnAccentInk : Colors.White;
    }

    private static Color shadeDark, shadeLight;

    /// The fill the styles paint <paramref name="picked"/> with on a dark or light surface (BrushAccent's colour; for the program's own accent
    /// Windows' own shade). For drawing outside the themed styles, such as the OSD.
    public static Color Shade(Color picked, bool darkTheme) =>
        picked == Current ? (darkTheme ? shadeDark : shadeLight)
        : darkTheme ? ColorMath.AccentShade(picked, darkTheme: true, ShadeScale) : ForLightTheme(picked);

    /// Applies the saved choice and starts listening to Windows' colour change notice and to a new background (once, for the program's life).
    public static void Initialize(string name, string? customHex, int intensity)
    {
        // 50 was the old default, saved by everyone who never moved the slider; it now means "the new default" at start-up.
        Take(customHex, intensity == WindowsIntensity ? DefaultIntensity : intensity);
        Apply(name);
        if (Application.Current?.PlatformSettings is { } platform) platform.ColorValuesChanged += OnWindowsColours;
        SurfaceService.Changed += () => Apply(Name); // the washes are blended into the page colour
    }

    /// Uses a saved choice, custom colour and intensity read again (an import wrote the settings): a custom colour that is not "#RRGGBB"
    /// falls back to the default one, an intensity out of range is clamped.
    public static void Load(string name, string? customHex, int intensity)
    {
        Take(customHex, intensity);
        Set(name);
    }

    private static void Take(string? customHex, int intensity)
    {
        Custom = ColorMath.ParseHex(customHex) ?? Color.Parse(DefaultCustomHex);
        Intensity = Math.Clamp(intensity, 0, MaxIntensity);
    }

    public static void Set(string name)
    {
        Apply(name);
        Changed?.Invoke();
    }

    /// Makes <paramref name="colour"/> the custom colour and uses it.
    public static void SetCustom(Color colour)
    {
        Custom = Color.FromRgb(colour.R, colour.G, colour.B);
        Set(CustomName);
    }

    /// Sets how strongly every choice is shaded (clamped to 0 to <see cref="MaxIntensity"/>).
    public static void SetIntensity(int intensity)
    {
        Intensity = Math.Clamp(intensity, 0, MaxIntensity);
        Set(Name);
    }

    /// The share of Windows' shade amount the intensity gives (1 at the default, 2 at the softest, 0 at the strongest).
    private static double ShadeScale => (MaxIntensity - Intensity) / (double)WindowsIntensity;

    /// Windows' accent (or its theme) changed: the Windows accent choice and its gallery dot follow.
    private static void OnWindowsColours(object? sender, PlatformColorValues e) =>
        Dispatcher.UIThread.Post(() => Guard.Run("Windows accent change", () => Set(Name)));

    /// Windows' accent colour; Blue when Windows does not report one (older Windows).
    private static Color WindowsAccent() =>
        Application.Current?.PlatformSettings?.GetColorValues().AccentColor1 is { A: > 0 } accent
            ? Color.FromRgb(accent.R, accent.G, accent.B)
            : Resolve(FallbackName);

    private static void Apply(string name)
    {
        Name = Choices.Contains(name) ? name : WindowsName;
        if (Application.Current is not { } app) return;
        var picked = Current;
        // Windows' own shades of its accent when it can say them (a cheap in-process WinRT call) at Windows' own intensity, else the same shades worked out.
        var windows = Name == WindowsName && Intensity == WindowsIntensity
            && Application.Current.PlatformSettings?.GetColorValues().AccentColor1 is { A: > 0 } ? AccentShades.Read() : null;
        var dark = windows is { } w ? Color.FromUInt32(w.ForDark) : ColorMath.AccentShade(picked, darkTheme: true, ShadeScale);
        var light = windows is { } v ? Color.FromUInt32(v.ForLight) : ForLightTheme(picked);
        (shadeDark, shadeLight) = (dark, light);

        Update(app, ThemeVariant.Dark, SurfaceService.PageOf(ThemeVariant.Dark), dark, 0.10, 0.18, Colors.White, 0.15);
        Update(app, ThemeVariant.Light, SurfaceService.PageOf(ThemeVariant.Light), light, 0.09, 0.20, Colors.White, 0.12);
    }

    /// The light theme's accent: Windows' darker shade, and darker still for a very light accent (White) so it shows on a light page.
    private static Color ForLightTheme(Color accent)
    {
        var shade = ColorMath.AccentShade(accent, darkTheme: false, ShadeScale);
        var luminance = ColorMath.Luminance(shade);
        return luminance > Palette.LightAccentMaxLuminance ? ColorMath.Mix(shade, Colors.Black, 1 - Palette.LightAccentMaxLuminance / luminance) : shade;
    }

    private static void Update(Application app, ThemeVariant variant, Color background, Color accent,
        double soft, double hover, Color lift, double liftAmount)
    {
        var over = ColorMath.Mix(accent, lift, liftAmount);
        var pressed = ColorMath.Mix(accent, Colors.Black, 0.12);
        var onAccent = OnAccent(accent);
        void Put(string key, Color color) => Written.Set(app, variant, key, color);

        Put("BrushAccent", accent);
        Put("BrushOnAccent", onAccent);
        Put("BrushAccentSoft", ColorMath.Mix(background, accent, soft));
        Put("BrushAccentHover", ColorMath.Mix(background, accent, hover));
        Put("BrushAccentEdge", ColorMath.Mix(background, accent, Palette.AccentEdgeStrength));
        Put("BrushAccentBright", over); // an accent fill under the pointer (a lit tray tile)

        foreach (var part in new[] { "Fill", "Stroke" })
        {
            Put($"ToggleSwitch{part}On", accent);
            Put($"ToggleSwitch{part}OnPointerOver", over);
            Put($"ToggleSwitch{part}OnPressed", pressed);
        }
        // The switch's knob sits on the accent fill, so it takes the on-accent colour (white, dark on a light accent).
        foreach (var state in new[] { "", "PointerOver", "Pressed" }) Put($"ToggleSwitchKnobFillOn{state}", onAccent);
        PutFluentAccent(app, variant, accent);
        ThemeBrushes.DictionaryOf(app, variant)[GlowKey] = new BoxShadows(new BoxShadow
        {
            Blur = Palette.GlowBlur,
            Spread = Palette.GlowSpread,
            Color = Color.FromArgb((byte)(Palette.GlowOpacity * byte.MaxValue), accent.R, accent.G, accent.B),
        });
    }

    /// Fluent paints its own controls (CheckBox, RadioButton, Slider, ProgressBar, focus lines...) from SystemAccentColor and its six shades, which
    /// default to Windows' accent; they are written here so those controls follow the program's accent. Fluent reads Light2 on the dark theme and
    /// Dark1 on the light one, so those two are the accent itself and the rest step lighter or darker from it.
    private static void PutFluentAccent(Application app, ThemeVariant variant, Color accent)
    {
        var dict = ThemeBrushes.DictionaryOf(app, variant);
        Color Step(Color to, int n) => ColorMath.Mix(accent, to, Palette.AccentShadeStep * n);
        var dark = variant == ThemeVariant.Dark;
        dict["SystemAccentColor"] = accent;
        dict["SystemAccentColorDark1"] = dark ? Step(Colors.Black, 2) : accent;
        dict["SystemAccentColorDark2"] = Step(Colors.Black, dark ? 3 : 1);
        dict["SystemAccentColorDark3"] = Step(Colors.Black, dark ? 4 : 2);
        dict["SystemAccentColorLight1"] = dark ? Step(Colors.Black, 1) : Step(Colors.White, 1);
        dict["SystemAccentColorLight2"] = dark ? accent : Step(Colors.White, 2);
        dict["SystemAccentColorLight3"] = Step(Colors.White, dark ? 1 : 3);
    }

    /// The soft accent glow around a lit button (Button.pill.glow, the Mixer's FX button while effects run).
    private const string GlowKey = "AccentGlow";
}
