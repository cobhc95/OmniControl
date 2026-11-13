using OmniControl.App.Osd;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One slider-type control of a display, described once: what it is called, which pop-up shows it, its range and the value a
/// double-click returns it to. The card, its up/down hotkey actions (and so the Shortcuts page, the mouse and gesture pickers and search)
/// and the OSD element are all made from this entry; nothing else lists the control.
/// </summary>
internal sealed record LevelSpec(string Key, string Label, string Keywords, string Osd, OsdLevelIcon Icon, int Min, int Max, int Default)
{
    /// <summary>The value as a 0-100 share of the control's range, which is what the OSD draws.</summary>
    public int OsdPercent(int value) => Percent.OfRange(value, Min, Max);
}

/// <summary>A control the monitor itself carries: a <see cref="LevelSpec"/> and the VCP code that reads and writes it over DDC/CI.</summary>
internal sealed record HardwareLevel(LevelSpec Spec, byte Code);

/// <summary>Every control of the Displays page, in the order the cards appear.</summary>
internal static class DisplayControls
{
    /// <summary>The two groups of the page; they head the registered actions and the search results too.</summary>
    public const string HardwareGroup = "Hardware", SoftwareGroup = "Software";

    private const string Screen = "monitor display screen ";

    public static readonly IReadOnlyList<HardwareLevel> Hardware = new HardwareLevel[]
    {
        Level("brightness", "Brightness", "brightness backlight", OsdService.BrightnessElement, OsdLevelIcon.Sun, 50, VcpCode.Brightness),
        Level("contrast", "Contrast", "contrast", "Contrast", OsdLevelIcon.Disc, 50, VcpCode.Contrast),
        Level("red", "Red gain", "red gain colour color white balance rgb", "Red gain", OsdLevelIcon.Disc, 100, VcpCode.RedGain),
        Level("green", "Green gain", "green gain colour color white balance rgb", "Green gain", OsdLevelIcon.Disc, 100, VcpCode.GreenGain),
        Level("blue", "Blue gain", "blue gain colour color white balance rgb", "Blue gain", OsdLevelIcon.Disc, 100, VcpCode.BlueGain),
        Level("saturation", "Saturation", "saturation colour color vibrance", "Saturation", OsdLevelIcon.Disc, 50, VcpCode.Saturation),
        Level("sharpness", "Sharpness", "sharpness sharp", "Sharpness", OsdLevelIcon.Disc, 50, VcpCode.Sharpness),
        Level("speaker", "Speaker volume", "speaker volume sound monitor audio", "Speaker volume", OsdLevelIcon.Speaker, 50, VcpCode.SpeakerVolume),
    };

    public static readonly LevelSpec SoftwareBrightness = new("brightness", "Digital brightness", Screen + "digital software gamma brightness dim darker",
        "Digital brightness", OsdLevelIcon.Sun, SoftwareColor.BrightnessMin, SoftwareColor.BrightnessMax, SoftwareColor.BrightnessNeutral);

    public static readonly LevelSpec SoftwareContrast = new("contrast", "Digital contrast", Screen + "digital software gamma contrast",
        "Digital contrast", OsdLevelIcon.Disc, SoftwareColor.ContrastMin, SoftwareColor.ContrastMax, SoftwareColor.ContrastNeutral);

    /// <summary>Warm (negative) to cool (positive) around the neutral white point; <see cref="SoftwareColor.KelvinOfWarmth"/> turns it into kelvin.</summary>
    public static readonly LevelSpec SoftwareWarmth = new("temperature", "Colour temperature", Screen + "digital software colour color temperature warm cool night light kelvin",
        "Colour temperature", OsdLevelIcon.Thermometer, SoftwareColor.WarmthMin, SoftwareColor.WarmthMax, 0);

    /// <summary>Saturation belongs to the whole desktop (Windows' colour effect cannot be set per display), so it has no monitor.</summary>
    public static readonly LevelSpec SoftwareSaturation = new("saturation", "Saturation (all displays)", Screen + "digital software saturation colour color greyscale",
        "Digital saturation", OsdLevelIcon.Disc, ColorEffect.SaturationMin, ColorEffect.SaturationMax,
        ColorEffect.SaturationNeutral);

    /// <summary>
    /// The part of a software control's action ids and gesture level id ("sw.temperature"), kept apart from the hardware control of the same
    /// key. Stored by hotkeys.json and gestures.json, so it never changes. A hardware control's is its <see cref="LevelSpec.Key"/>, which is
    /// also the stored id: "brightness" and "contrast" were the original actions, and no key is "volume", which the Volume page owns.
    /// </summary>
    public static string SoftwareKey(LevelSpec spec) => $"sw.{spec.Key}";

    /// <summary>Every level control, in card order: what the OSD page needs to declare.</summary>
    public static IEnumerable<LevelSpec> All =>
        Hardware.Select(h => h.Spec).Append(SoftwareBrightness).Append(SoftwareContrast).Append(SoftwareWarmth).Append(SoftwareSaturation);

    private static HardwareLevel Level(string key, string label, string keywords, string osd, OsdLevelIcon icon, int @default, byte code) =>
        new(new LevelSpec(key, label, Screen + keywords, osd, icon, Percent.Min, Percent.Max, @default), code);
}
