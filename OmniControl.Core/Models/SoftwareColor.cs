namespace OmniControl.Core.Models;

/// <summary>
/// The picture changes OmniControl makes in software on one display (a gamma ramp), as slider values. The ranges are limited so the
/// screen can never go black (and so Windows accepts the ramp); the neutral values leave the picture as it was.
/// </summary>
public readonly record struct SoftwareColor(int Brightness, int Contrast, int Temperature)
{
    public const int BrightnessMin = 40, BrightnessMax = 100, BrightnessNeutral = 100;
    public const int ContrastMin = 50, ContrastMax = 150, ContrastNeutral = 100;

    /// <summary>Colour temperature in kelvin; the neutral value is the usual white point of a screen (D65).</summary>
    public const int TemperatureMin = 3000, TemperatureMax = 10000, TemperatureNeutral = 6500;

    /// <summary>The slider form of the temperature: 0 is neutral, negative warmer (towards 3000 K), positive cooler (towards 10000 K).</summary>
    public const int WarmthMin = -100, WarmthMax = 100;

    /// <summary>Kelvin for a slider position between <see cref="WarmthMin"/> and <see cref="WarmthMax"/>.</summary>
    public static int KelvinOfWarmth(int warmth)
    {
        warmth = Math.Clamp(warmth, WarmthMin, WarmthMax);
        return warmth < 0
            ? TemperatureNeutral + warmth * (TemperatureNeutral - TemperatureMin) / -WarmthMin
            : TemperatureNeutral + warmth * (TemperatureMax - TemperatureNeutral) / WarmthMax;
    }

    public static SoftwareColor Neutral { get; } = new(BrightnessNeutral, ContrastNeutral, TemperatureNeutral);

    public bool IsNeutral => this == Neutral;

    public SoftwareColor Clamped() => new(
        Math.Clamp(Brightness, BrightnessMin, BrightnessMax),
        Math.Clamp(Contrast, ContrastMin, ContrastMax),
        Math.Clamp(Temperature, TemperatureMin, TemperatureMax));
}
