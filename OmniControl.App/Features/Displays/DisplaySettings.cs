using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Displays;

/// The slider positions of one display's software picture (what a preset keeps; the warmth is the slider's -100..+100, not kelvin).
public sealed class PictureValues
{
    public int Brightness { get; set; } = SoftwareColor.BrightnessNeutral;
    public int Contrast { get; set; } = SoftwareColor.ContrastNeutral;
    public int Warmth { get; set; }
    public int Red { get; set; } = SoftwareColor.GainNeutral;
    public int Green { get; set; } = SoftwareColor.GainNeutral;
    public int Blue { get; set; } = SoftwareColor.GainNeutral;
    public int Gamma { get; set; } = SoftwareColor.GammaNeutral;
    public int Black { get; set; } = SoftwareColor.BlackNeutral;
    public int White { get; set; } = SoftwareColor.WhiteNeutral;

    public bool SameAs(PictureValues other) =>
        (Brightness, Contrast, Warmth, Red, Green, Blue, Gamma, Black, White)
        == (other.Brightness, other.Contrast, other.Warmth, other.Red, other.Green, other.Blue, other.Gamma, other.Black, other.White);

    public SoftwareColor ToColor() =>new(Brightness, Contrast, SoftwareColor.KelvinOfWarmth(Warmth), Red, Green, Blue, Gamma, Black, White);
}

/// A named set of every software picture value: one picture per display (by Windows' name for it), the first one for a display it has none for, and the desktop's colour effect.
public sealed class DisplayPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, PictureValues> Displays { get; set; } = new();
    public PictureValues Any { get; set; } = new();
    public int Saturation { get; set; } = ColorMatrix.SaturationNeutral;
    public bool Invert { get; set; }

    /// The picture for a display: its own, else the one kept for any.
    public PictureValues For(string deviceName) => Displays.TryGetValue(deviceName, out var own) ? own : Any;

    /// True when this preset shows the same picture as <paramref name="current"/> on every display <paramref name="current"/> has a picture for, and the same colour effect.
    public bool Matches(DisplayPreset current) =>
        Saturation == current.Saturation && Invert == current.Invert
        && current.Displays.All(shown => For(shown.Key).SameAs(shown.Value));
}

/// Warm light: the temperature the picture warms to (<see cref="Kelvin"/>) and the times last chosen (<see cref="Start"/> to <see cref="End"/>, minutes since
/// midnight), used when its automation rule is made again. Whether and when it runs is that rule's (<see cref="WarmLightScheduler"/>).
public sealed class WarmLightSettings
{
    /// Read once, to make the rule from a schedule saved before warm light was a rule.
    public bool Enabled { get; set; }

    /// The rule was made from these settings (once; a deleted rule is not made again).
    public bool InRule { get; set; }

    public int Start { get; set; } = 21 * 60;
    public int End { get; set; } = 7 * 60;
    public int Kelvin { get; set; } = 3400;
}

/// What the Displays page keeps between runs (display-settings.json): the warm-light schedule, the picture presets, the monitor presets
/// and, per monitor (by its model name), the monitor preset applied to it last. A file without the monitor parts loads as before.
public sealed class DisplaySettings
{
    public WarmLightSettings WarmLight { get; set; } = new();
    public List<DisplayPreset> Presets { get; set; } = new();
    public List<MonitorPreset> MonitorPresets { get; set; } = new();
    public Dictionary<string, string> MonitorLastPreset { get; set; } = new();
}
