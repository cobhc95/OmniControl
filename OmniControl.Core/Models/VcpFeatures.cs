namespace OmniControl.Core.Models;

/// <summary>Whether a feature is a level (a number between 0 and the monitor's own maximum) or a choice (one of a list of values).</summary>
public enum VcpKind { Level, Choice }

/// <summary>
/// One monitor feature OmniControl can offer: its VCP code, a stable key (action and gesture ids are made from it, so it never
/// changes), its name and search words, and the value a double-click returns a level to (null when the standard sets none: the card
/// then returns to the value the monitor had when it was read).
/// </summary>
public sealed record VcpFeature(byte Code, string Key, string Label, string Keywords, VcpKind Kind, int? Default)
{
    /// <summary>The part of the monitor's controls it belongs to (one of <see cref="VcpSections"/>): what lists group its actions under.</summary>
    public string Section { get; init; } = VcpSections.Other;

    /// <summary>The keys "Suggest" offers for a level's Up and Down actions (never bound by default); null: none.</summary>
    public string? SuggestedUp { get; init; }
    public string? SuggestedDown { get; init; }
}

/// <summary>The sections the monitor features are grouped in, in the order lists show them.</summary>
public static class VcpSections
{
    public const string Other = "Other";
    public const string BrightnessContrast = "Brightness and contrast";
    public const string Colour = "Colour (RGB and gamma)";
    public const string Levels = "Levels (black and backlight)";
    public const string Axes = "Colour axes (hue and saturation)";
    public const string SoundInput = "Sound and input";
    public const string Presets = "Presets";
    public const string PowerMode = "Power and mode";
}

/// <summary>
/// The list of monitor features the Displays page can offer, by VESA MCCS (Monitor Control Command Set) VCP code: the ONE place that
/// knows their names. Codes and names are the continuous and table controls of MCCS 2.2a; written from the standard's feature list as
/// remembered, with no copy of the document at hand, so a name that looks wrong is a fix here and nowhere else. A monitor reports
/// which of them it implements (<see cref="MonitorCapabilities"/>); the page offers the ones it reports and lists the rest as not available.
/// </summary>
public static class VcpFeatures
{
    private static VcpFeature Level(byte code, string key, string label, string keywords, int? @default = null) =>
        new(code, key, label, keywords, VcpKind.Level, @default);

    private static VcpFeature Choice(byte code, string key, string label, string keywords) =>
        new(code, key, label, keywords, VcpKind.Choice, null);

    private const string Rgb = "colour color rgb white balance ";

    public static readonly IReadOnlyList<VcpFeature> All = new[]
    {
        Level(VcpCode.Brightness, "brightness", "Brightness", "brightness backlight luminance", 50) with
        {
            Section = VcpSections.BrightnessContrast, SuggestedUp = "Ctrl+Shift+Up", SuggestedDown = "Ctrl+Shift+Down",
        },
        Level(VcpCode.Contrast, "contrast", "Contrast", "contrast", 50) with { Section = VcpSections.BrightnessContrast },
        Level(VcpCode.RedGain, "red", "Red gain", Rgb + "red gain video", 100) with { Section = VcpSections.Colour },
        Level(VcpCode.GreenGain, "green", "Green gain", Rgb + "green gain video", 100) with { Section = VcpSections.Colour },
        Level(VcpCode.BlueGain, "blue", "Blue gain", Rgb + "blue gain video", 100) with { Section = VcpSections.Colour },
        Level(VcpCode.Saturation, "saturation", "Saturation", "saturation colour color vibrance", 50) with { Section = VcpSections.Axes },
        Level(VcpCode.Sharpness, "sharpness", "Sharpness", "sharpness sharp edge", 50) with { Section = VcpSections.BrightnessContrast },
        Level(VcpCode.SpeakerVolume, "speaker", "Speaker volume", "speaker volume sound monitor audio", 50) with { Section = VcpSections.SoundInput },
        Level(0x6C, "black.red", "Red black level", Rgb + "red black level shadows") with { Section = VcpSections.Levels },
        Level(0x6E, "black.green", "Green black level", Rgb + "green black level shadows") with { Section = VcpSections.Levels },
        Level(0x70, "black.blue", "Blue black level", Rgb + "blue black level shadows") with { Section = VcpSections.Levels },
        Level(0x6B, "backlight.white", "Backlight (white)", "backlight white level") with { Section = VcpSections.Levels },
        Level(0x6D, "backlight.red", "Backlight (red)", "backlight red level " + Rgb) with { Section = VcpSections.Levels },
        Level(0x6F, "backlight.green", "Backlight (green)", "backlight green level " + Rgb) with { Section = VcpSections.Levels },
        Level(0x71, "backlight.blue", "Backlight (blue)", "backlight blue level " + Rgb) with { Section = VcpSections.Levels },
        Level(0x72, "gamma", "Gamma", "gamma midtones curve") with { Section = VcpSections.Colour },
        Level(0x90, "hue", "Hue", "hue tint colour color") with { Section = VcpSections.Axes },
        Level(0x59, "sixsat.red", "Red saturation", "six axis saturation red colour color") with { Section = VcpSections.Axes },
        Level(0x5A, "sixsat.yellow", "Yellow saturation", "six axis saturation yellow colour color") with { Section = VcpSections.Axes },
        Level(0x5B, "sixsat.green", "Green saturation", "six axis saturation green colour color") with { Section = VcpSections.Axes },
        Level(0x5C, "sixsat.cyan", "Cyan saturation", "six axis saturation cyan colour color") with { Section = VcpSections.Axes },
        Level(0x5D, "sixsat.blue", "Blue saturation", "six axis saturation blue colour color") with { Section = VcpSections.Axes },
        Level(0x5E, "sixsat.magenta", "Magenta saturation", "six axis saturation magenta colour color") with { Section = VcpSections.Axes },
        Level(0x9B, "sixhue.red", "Red hue", "six axis hue red colour color") with { Section = VcpSections.Axes },
        Level(0x9C, "sixhue.yellow", "Yellow hue", "six axis hue yellow colour color") with { Section = VcpSections.Axes },
        Level(0x9D, "sixhue.green", "Green hue", "six axis hue green colour color") with { Section = VcpSections.Axes },
        Level(0x9E, "sixhue.cyan", "Cyan hue", "six axis hue cyan colour color") with { Section = VcpSections.Axes },
        Level(0x9F, "sixhue.blue", "Blue hue", "six axis hue blue colour color") with { Section = VcpSections.Axes },
        Level(0xA0, "sixhue.magenta", "Magenta hue", "six axis hue magenta colour color") with { Section = VcpSections.Axes },

        Choice(VcpCode.ColorPreset, "preset", "Colour preset", "colour color preset temperature srgb") with { Section = VcpSections.Presets },
        Choice(VcpCode.InputSource, "input", "Input source", "input source switch hdmi displayport dvi vga kvm") with { Section = VcpSections.SoundInput },
        Choice(0xDC, "mode", "Display mode", "display mode game movie picture preset application") with { Section = VcpSections.PowerMode },
        Choice(0x8D, "mute", "Speaker mute", "speaker mute sound audio") with { Section = VcpSections.SoundInput },
    };

    public static IEnumerable<VcpFeature> Levels => All.Where(f => f.Kind == VcpKind.Level);

    public static IEnumerable<VcpFeature> Choices => All.Where(f => f.Kind == VcpKind.Choice);

    /// <summary>
    /// The codes always asked first: the ones nearly every monitor with DDC/CI answers, so a monitor with DDC/CI off is found quickly.
    /// Anything else is read when the monitor's capabilities string lists it (or when it gave no list).
    /// </summary>
    public static readonly IReadOnlyList<byte> Core = new[]
    {
        VcpCode.Brightness, VcpCode.Contrast, VcpCode.ColorPreset, VcpCode.InputSource, VcpCode.PowerMode,
    };
}
