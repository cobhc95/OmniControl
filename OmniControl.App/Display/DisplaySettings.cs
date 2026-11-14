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

    public SoftwareColor ToColor() => new(Brightness, Contrast, SoftwareColor.KelvinOfWarmth(Warmth), Red, Green, Blue, Gamma, Black, White);
}

/// <summary>A named set of every software picture value: one picture per display (by Windows' name for it), the first one for a display it has none for, and the desktop's colour effect.</summary>
public sealed class DisplayPreset
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, PictureValues> Displays { get; set; } = new();
    public PictureValues Any { get; set; } = new();
    public int Saturation { get; set; } = ColorMatrix.SaturationNeutral;
    public bool Invert { get; set; }

    /// <summary>The picture for a display: its own, else the one kept for any.</summary>
    public PictureValues For(string deviceName) => Displays.TryGetValue(deviceName, out var own) ? own : Any;
}

/// <summary>The warm-light schedule: from <see cref="Start"/> to <see cref="End"/> (minutes since midnight) the picture warms to <see cref="Kelvin"/>.</summary>
public sealed class WarmLightSettings
{
    public bool Enabled { get; set; }
    public int Start { get; set; } = 21 * 60;
    public int End { get; set; } = 7 * 60;
    public int Kelvin { get; set; } = 3400;
}

/// <summary>What the Displays page keeps between runs (display-settings.json): the warm-light schedule and the picture presets.</summary>
public sealed class DisplaySettings
{
    public WarmLightSettings WarmLight { get; set; } = new();
    public List<DisplayPreset> Presets { get; set; } = new();
}
