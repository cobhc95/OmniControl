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
    /// <see cref="Palette.AccentLightenDark"/> of the way to white, or by <see cref="Palette.AccentDarkenLight"/> towards black).
    public static Color AccentShade(Color accent, bool darkTheme)
    {
        var hsl = accent.ToHsl();
        var lightness = darkTheme ? hsl.L + (1 - hsl.L) * Palette.AccentLightenDark : hsl.L * (1 - Palette.AccentDarkenLight);
        return new HslColor(hsl.A, hsl.H, hsl.S, lightness).ToRgb();
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
/// when Windows reports none, it is Blue. Nothing else in the program hard-codes an accent colour: use <see cref="Current"/> (the colour as
/// picked) in code or the DynamicResource brushes in XAML.
public static class AccentService
{
    /// The choice that follows Windows' accent colour; first in <see cref="Names"/>.
    public const string WindowsName = "Windows accent";

    /// The choice "Windows accent" falls back to when Windows reports no accent.
    private const string FallbackName = "Blue";

    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new[]
    {
        ("Orange", "#F28C48"), ("Blue", "#4C9BE8"), ("Green", "#5DBE7A"), ("Pink", "#E86FA6"),
        ("Purple", "#9B7BE8"), ("Red", Palette.DangerHex), ("Yellow", "#E8C547"), ("Teal", "#3FB8B0"), ("White", Palette.LightHex),
    };

    /// Every choice in the order the galleries show it: Windows accent, then the fixed colours.
    public static IReadOnlyList<string> Names { get; } = Presets.Select(p => p.Name).Prepend(WindowsName).ToList();

    private static readonly ThemeBrushes Written = new();

    public static string Name { get; private set; } = WindowsName;

    /// The chosen accent as picked (before the theme's shade).
    public static Color Current => Resolve(Name);

    public static event Action? Changed;

    public static Color Resolve(string name) => name == WindowsName
        ? WindowsAccent()
        : Color.Parse((Presets.FirstOrDefault(p => p.Name == name) is { Hex: not null } p ? p : Presets[0]).Hex);

    /// The words or glyph drawn on a fill of <paramref name="fill"/>, as Windows does: near-black ink (<see cref="Palette.OnAccentInk"/>) or
    /// white, whichever reads better on it (so dark ink on the dark theme's light shades, white on the light theme's darker ones).
    public static Color OnAccent(Color fill)
    {
        var ink = ColorMath.Mix(fill, Color.FromRgb(Palette.OnAccentInk.R, Palette.OnAccentInk.G, Palette.OnAccentInk.B), Palette.OnAccentInk.A / 255.0);
        return ColorMath.Contrast(ink, fill) >= ColorMath.Contrast(Colors.White, fill) ? Palette.OnAccentInk : Colors.White;
    }

    /// Applies the saved choice and starts listening to Windows' colour change notice and to a new background (once, for the program's life).
    public static void Initialize(string name)
    {
        Apply(name);
        if (Application.Current?.PlatformSettings is { } platform) platform.ColorValuesChanged += OnWindowsColours;
        SurfaceService.Changed += () => Apply(Name); // the washes are blended into the page colour
    }

    public static void Set(string name)
    {
        Apply(name);
        Changed?.Invoke();
    }

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
        Name = Names.Contains(name) ? name : WindowsName;
        if (Application.Current is not { } app) return;
        var picked = Current;
        // Windows' own shades of its accent when it can say them (a cheap in-process WinRT call), else the same shades worked out.
        var windows = Name == WindowsName && Application.Current.PlatformSettings?.GetColorValues().AccentColor1 is { A: > 0 } ? AccentShades.Read() : null;
        var dark = windows is { } w ? Color.FromUInt32(w.ForDark) : ColorMath.AccentShade(picked, darkTheme: true);
        var light = windows is { } v ? Color.FromUInt32(v.ForLight) : ForLightTheme(picked);

        Update(app, ThemeVariant.Dark, SurfaceService.PageOf(ThemeVariant.Dark), dark, 0.10, 0.18, Colors.White, 0.15);
        Update(app, ThemeVariant.Light, SurfaceService.PageOf(ThemeVariant.Light), light, 0.09, 0.20, Colors.White, 0.12);
    }

    /// The light theme's accent: Windows' darker shade, and darker still for a very light accent (White) so it shows on a light page.
    private static Color ForLightTheme(Color accent)
    {
        var shade = ColorMath.AccentShade(accent, darkTheme: false);
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
        ThemeBrushes.DictionaryOf(app, variant)[GlowKey] = new BoxShadows(new BoxShadow
        {
            Blur = Palette.GlowBlur,
            Spread = Palette.GlowSpread,
            Color = Color.FromArgb((byte)(Palette.GlowOpacity * byte.MaxValue), accent.R, accent.G, accent.B),
        });
    }

    /// The soft accent glow around a lit button (Button.pill.glow, the Mixer's FX button while effects run).
    private const string GlowKey = "AccentGlow";
}
