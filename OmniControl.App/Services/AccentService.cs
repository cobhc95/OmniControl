using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OmniControl.App.Services;

/// <summary>Colour helpers shared by the accent service and the OSD.</summary>
public static class ColorMath
{
    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}

/// <summary>
/// The one place the program's accent colour lives. Pick a preset with <see cref="Set"/>; every accent brush the styles use
/// (BrushAccent, BrushAccentSoft, BrushAccentHover and the toggle-switch fills) is derived from it for both the dark and the
/// light theme, and updates live. Nothing else in the program hard-codes an accent colour: use <see cref="Current"/> in code
/// or the DynamicResource brushes in XAML.
/// </summary>
public static class AccentService
{
    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new[]
    {
        ("Orange", "#F28C48"), ("Blue", "#4C9BE8"), ("Green", "#5DBE7A"), ("Pink", "#E86FA6"),
        ("Purple", "#9B7BE8"), ("Red", "#E5534B"), ("Yellow", "#E8C547"), ("Teal", "#3FB8B0"),
    };

    public static IReadOnlyList<string> Names { get; } = Presets.Select(p => p.Name).ToList();

    // The two theme backgrounds the soft/hover tints are blended into (the page background of each theme).
    private static readonly Color DarkBackground = Color.Parse("#161616");
    private static readonly Color LightBackground = Colors.White;

    private static readonly Dictionary<(ThemeVariant, string), SolidColorBrush> Brushes = new();

    public static string Name { get; private set; } = Presets[0].Name;

    /// <summary>The chosen accent as picked (the dark-theme value).</summary>
    public static Color Current => Resolve(Name);

    public static event Action? Changed;

    public static Color Resolve(string name) =>
        Color.Parse((Presets.FirstOrDefault(p => p.Name == name) is { Hex: not null } p ? p : Presets[0]).Hex);

    public static void Initialize(string name) => Apply(name);

    public static void Set(string name)
    {
        Apply(name);
        Changed?.Invoke();
    }

    private static void Apply(string name)
    {
        Name = Presets.Any(p => p.Name == name) ? name : Presets[0].Name;
        if (Application.Current is not { } app) return;
        var baseColor = Current;

        Update(app, ThemeVariant.Dark, DarkBackground, baseColor, 0.10, 0.18, Colors.White, 0.15);
        Update(app, ThemeVariant.Light, LightBackground, ColorMath.Mix(baseColor, Colors.Black, 0.14), 0.09, 0.20, Colors.White, 0.12);
    }

    private static void Update(Application app, ThemeVariant variant, Color background, Color accent,
        double soft, double hover, Color lift, double liftAmount)
    {
        var over = ColorMath.Mix(accent, lift, liftAmount);
        var pressed = ColorMath.Mix(accent, Colors.Black, 0.12);

        Set(app, variant, "BrushAccent", accent);
        Set(app, variant, "BrushAccentSoft", ColorMath.Mix(background, accent, soft));
        Set(app, variant, "BrushAccentHover", ColorMath.Mix(background, accent, hover));

        foreach (var part in new[] { "Fill", "Stroke" })
        {
            Set(app, variant, $"ToggleSwitch{part}On", accent);
            Set(app, variant, $"ToggleSwitch{part}OnPointerOver", over);
            Set(app, variant, $"ToggleSwitch{part}OnPressed", pressed);
        }
    }

    private static void Set(Application app, ThemeVariant variant, string key, Color color)
    {
        if (Brushes.TryGetValue((variant, key), out var brush)) { brush.Color = color; return; }

        brush = new SolidColorBrush(color);
        Brushes[(variant, key)] = brush;
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary dict)
            app.Resources.ThemeDictionaries[variant] = dict = new ResourceDictionary();
        dict[key] = brush;
    }
}
