using OmniControl.App.Display;
using OmniControl.App.Osd;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One slider-type control of a display, described once: what it is called, which pop-up shows it, its range and the value a
/// double-click returns it to. The card, its up/down hotkey actions (and so the Shortcuts page, the mouse and gesture pickers and search)
/// and the OSD element are all made from this entry; nothing else lists the control.
/// </summary>
internal sealed record LevelSpec(string Key, string Label, string Keywords, string Osd, OsdLevelIcon Icon, int Min, int Max, int Default, string Section)
{
    /// <summary>The value as a 0-100 share of the control's range, which is what the OSD draws.</summary>
    public int OsdPercent(int value) => Percent.OfRange(value, Min, Max);
}

/// <summary>
/// A control the monitor itself carries: a <see cref="LevelSpec"/> and the VCP code that reads and writes it over DDC/CI.
/// <paramref name="ResetToReading"/>: the standard names no default, so a double-click returns the slider to the value the monitor had when it was read.
/// </summary>
internal sealed record HardwareLevel(LevelSpec Spec, byte Code, bool ResetToReading);

/// <summary>A monitor feature that is one of a list of values (colour preset, input source...): the feature and the title of its "next" action.</summary>
internal sealed record HardwareChoice(VcpFeature Feature, string ActionTitle);

/// <summary>A software control of one display: its <see cref="LevelSpec"/> and where its value sits in a <see cref="PictureValues"/> (what a preset keeps).</summary>
internal sealed record SoftwareLevel(LevelSpec Spec, Func<PictureValues, int> Get, Action<PictureValues, int> Set);

/// <summary>Every control of the Displays page, in the order the cards appear.</summary>
internal static class DisplayControls
{
    /// <summary>The two groups of the page; they head the registered actions and the search results too.</summary>
    public const string HardwareGroup = "Hardware", SoftwareGroup = "Software";

    private const string Screen = "monitor display screen ";
    private const string Digital = Screen + "digital software gamma ";

    /// <summary>One entry per feature of <see cref="VcpFeatures"/> that is a level: the monitor reports which of them it has.</summary>
    public static readonly IReadOnlyList<HardwareLevel> Hardware = VcpFeatures.Levels.Select(f =>
        new HardwareLevel(new LevelSpec(f.Key, f.Label, Screen + f.Keywords, f.Label, IconOf(f), Percent.Min, Percent.Max, f.Default ?? Percent.Max / 2, f.Section),
            f.Code, f.Default is null)).ToList();

    /// <summary>One entry per feature of <see cref="VcpFeatures"/> that is a choice.</summary>
    public static readonly IReadOnlyList<HardwareChoice> HardwareChoices = VcpFeatures.Choices.Select(f => new HardwareChoice(f, $"Next {f.Label.ToLowerInvariant()}")).ToList();

    public static readonly LevelSpec SoftwareBrightness = new("brightness", "Digital brightness", Digital + "brightness dim darker",
        "Digital brightness", OsdLevelIcon.Sun, SoftwareColor.BrightnessMin, SoftwareColor.BrightnessMax, SoftwareColor.BrightnessNeutral, VcpSections.BrightnessContrast);

    public static readonly LevelSpec SoftwareContrast = new("contrast", "Digital contrast", Digital + "contrast",
        "Digital contrast", OsdLevelIcon.Disc, SoftwareColor.ContrastMin, SoftwareColor.ContrastMax, SoftwareColor.ContrastNeutral, VcpSections.BrightnessContrast);

    /// <summary>Warm (negative) to cool (positive) around the neutral white point; <see cref="SoftwareColor.KelvinOfWarmth"/> turns it into kelvin.</summary>
    public static readonly LevelSpec SoftwareWarmth = new("temperature", "Colour temperature", Digital + "colour color temperature warm cool night light kelvin",
        "Colour temperature", OsdLevelIcon.Thermometer, SoftwareColor.WarmthMin, SoftwareColor.WarmthMax, 0, VcpSections.Colour);

    /// <summary>The per-display software controls in card order (pairs: brightness and contrast, temperature and gamma, ...).</summary>
    public static readonly IReadOnlyList<SoftwareLevel> Software = new[]
    {
        new SoftwareLevel(SoftwareBrightness, v => v.Brightness, (v, x) => v.Brightness = x),
        new SoftwareLevel(SoftwareContrast, v => v.Contrast, (v, x) => v.Contrast = x),
        new SoftwareLevel(SoftwareWarmth, v => v.Warmth, (v, x) => v.Warmth = x),
        Soft("gamma", "Digital gamma", "gamma midtones curve", OsdLevelIcon.Disc, SoftwareColor.GammaMin, SoftwareColor.GammaMax, SoftwareColor.GammaNeutral,
            v => v.Gamma, (v, x) => v.Gamma = x),
        Soft("red", "Digital red gain", "red gain rgb white balance", OsdLevelIcon.Disc, SoftwareColor.GainMin, SoftwareColor.GainMax, SoftwareColor.GainNeutral,
            v => v.Red, (v, x) => v.Red = x),
        Soft("green", "Digital green gain", "green gain rgb white balance", OsdLevelIcon.Disc, SoftwareColor.GainMin, SoftwareColor.GainMax, SoftwareColor.GainNeutral,
            v => v.Green, (v, x) => v.Green = x),
        Soft("blue", "Digital blue gain", "blue gain rgb white balance", OsdLevelIcon.Disc, SoftwareColor.GainMin, SoftwareColor.GainMax, SoftwareColor.GainNeutral,
            v => v.Blue, (v, x) => v.Blue = x),
        Soft("black", "Digital black level", "black level lift shadows", OsdLevelIcon.Disc, SoftwareColor.BlackMin, SoftwareColor.BlackMax, SoftwareColor.BlackNeutral,
            v => v.Black, (v, x) => v.Black = x),
        Soft("white", "Digital white level", "white level limit highlights", OsdLevelIcon.Disc, SoftwareColor.WhiteMin, SoftwareColor.WhiteMax, SoftwareColor.WhiteNeutral,
            v => v.White, (v, x) => v.White = x),
    };

    /// <summary>Saturation belongs to the whole desktop (Windows' colour effect cannot be set per display), so it has no monitor.</summary>
    public static readonly LevelSpec SoftwareSaturation = new("saturation", "Saturation (all displays)", Screen + "digital software saturation colour color greyscale",
        "Digital saturation", OsdLevelIcon.Disc, ColorMatrix.SaturationMin, ColorMatrix.SaturationMax, ColorMatrix.SaturationNeutral, VcpSections.Axes);

    /// <summary>
    /// The part of a software control's action ids and gesture level id ("sw.temperature"), kept apart from the hardware control of the same
    /// key. Stored by hotkeys.json and gestures.json, so it never changes. A hardware control's is its <see cref="LevelSpec.Key"/>, which is
    /// also the stored id: "brightness" and "contrast" were the original actions, and no key is "volume", which the Volume page owns.
    /// </summary>
    public static string SoftwareKey(LevelSpec spec) => $"sw.{spec.Key}";

    /// <summary>Every level control, in card order: what the OSD page needs to declare.</summary>
    public static IEnumerable<LevelSpec> All =>
        Hardware.Select(h => h.Spec).Concat(Software.Select(s => s.Spec)).Append(SoftwareSaturation);

    private static OsdLevelIcon IconOf(VcpFeature feature) => feature.Code switch
    {
        VcpCode.Brightness => OsdLevelIcon.Sun,
        VcpCode.SpeakerVolume => OsdLevelIcon.Speaker,
        _ => OsdLevelIcon.Disc,
    };

    private static SoftwareLevel Soft(string key, string label, string keywords, OsdLevelIcon icon, int min, int max, int neutral,
        Func<PictureValues, int> get, Action<PictureValues, int> set) =>
        new(new LevelSpec(key, label, Digital + keywords, label, icon, min, max, neutral, key is "black" or "white" ? VcpSections.Levels : VcpSections.Colour), get, set);
}
