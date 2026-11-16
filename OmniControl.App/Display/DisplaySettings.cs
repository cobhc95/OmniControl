using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Display;

/// <summary>The slider positions of one display's software picture (what a preset keeps; the warmth is the slider's -100..+100, not kelvin).</summary>
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

/// <summary>A named set of every software picture value: one picture per display (by Windows' name for it), the first one for a display it has none for, and the desktop's colour effect.</summary>
public sealed class DisplayPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, PictureValues> Displays { get; set; } = new();
    public PictureValues Any { get; set; } = new();
    public int Saturation { get; set; } = ColorMatrix.SaturationNeutral;
    public bool Invert { get; set; }

    /// <summary>The picture for a display: its own, else the one kept for any.</summary>
    public PictureValues For(string deviceName) => Displays.TryGetValue(deviceName, out var own) ? own : Any;

    /// <summary>True when this preset shows the same picture as <paramref name="current"/> on every display <paramref name="current"/> has a picture for, and the same colour effect.</summary>
    public bool Matches(DisplayPreset current) =>
        Saturation == current.Saturation && Invert == current.Invert
        && current.Displays.All(shown => For(shown.Key).SameAs(shown.Value));
}

/// <summary>The warm-light schedule: from <see cref="Start"/> to <see cref="End"/> (minutes since midnight) the picture warms to <see cref="Kelvin"/>.</summary>
public sealed class WarmLightSettings
{
    public bool Enabled { get; set; }
    public int Start { get; set; } = 21 * 60;
    public int End { get; set; } = 7 * 60;
    public int Kelvin { get; set; } = 3400;
}

/// <summary>
/// What the Displays page keeps between runs (display-settings.json): the warm-light schedule, the picture presets, the monitor presets
/// and, per monitor (by its model name), the monitor preset applied to it last. A file without the monitor parts loads as before.
/// </summary>
public sealed class DisplaySettings
{
    public WarmLightSettings WarmLight { get; set; } = new();
    public List<DisplayPreset> Presets { get; set; } = new();
    public List<MonitorPreset> MonitorPresets { get; set; } = new();
    public Dictionary<string, string> MonitorLastPreset { get; set; } = new();
}
