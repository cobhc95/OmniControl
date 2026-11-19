namespace OmniControl.Core.Models;

/// The picture changes OmniControl makes in software on one display (a gamma ramp), as slider values. The ranges are limited so the
/// screen can never go black (and so Windows accepts the ramp); the neutral values leave the picture as it was. Saturation, grayscale
/// and invert are not here: a ramp treats each colour channel alone, so those are one colour matrix for the whole desktop (<c>ColorMatrix</c>).
public readonly record struct SoftwareColor(
    int Brightness, int Contrast, int Temperature,
    int Red = SoftwareColor.GainNeutral, int Green = SoftwareColor.GainNeutral, int Blue = SoftwareColor.GainNeutral,
    int Gamma = SoftwareColor.GammaNeutral, int Black = SoftwareColor.BlackNeutral, int White = SoftwareColor.WhiteNeutral)
{
    public const int BrightnessMin = 40, BrightnessMax = 100, BrightnessNeutral = 100;
    public const int ContrastMin = 50, ContrastMax = 150, ContrastNeutral = 100;

    /// Colour temperature in kelvin; the neutral value is the usual white point of a screen (D65).
    public const int TemperatureMin = 3000, TemperatureMax = 10000, TemperatureNeutral = 6500;

    /// The slider form of the temperature: 0 is neutral, negative warmer (towards 3000 K), positive cooler (towards 10000 K).
    public const int WarmthMin = -100, WarmthMax = 100;

    /// Red, green and blue gain in percent of the channel; 100 leaves it as it is, no channel goes below a fifth.
    public const int GainMin = 20, GainMax = 100, GainNeutral = 100;

    /// Gamma as a signed step: the curve's exponent is 1 + step / 100, so plus lifts the midtones and minus darkens them; 0 is neutral.
    public const int GammaMin = -50, GammaMax = 50, GammaNeutral = 0;

    /// Black level: how far the darkest colour is lifted, in percent of full scale (0 = as it is). With <see cref="WhiteMin"/> it leaves at least 40 percent of the range.
    public const int BlackMin = 0, BlackMax = 30, BlackNeutral = 0;

    /// White level: the brightest colour's limit in percent of full scale (100 = as it is).
    public const int WhiteMin = 70, WhiteMax = 100, WhiteNeutral = 100;

    /// Kelvin for a slider position between <see cref="WarmthMin"/> and <see cref="WarmthMax"/>.
    public static int KelvinOfWarmth(int warmth)
    {
        warmth = Math.Clamp(warmth, WarmthMin, WarmthMax);
        return warmth < 0
            ? TemperatureNeutral + warmth * (TemperatureNeutral - TemperatureMin) / -WarmthMin
            : TemperatureNeutral + warmth * (TemperatureMax - TemperatureNeutral) / WarmthMax;
    }

    public static SoftwareColor Neutral { get; } = new(BrightnessNeutral, ContrastNeutral, TemperatureNeutral);

    public bool IsNeutral => Clamped() == Neutral;

    public SoftwareColor Clamped() => new(
        Math.Clamp(Brightness, BrightnessMin, BrightnessMax),
        Math.Clamp(Contrast, ContrastMin, ContrastMax),
        Math.Clamp(Temperature, TemperatureMin, TemperatureMax),
        Math.Clamp(Red, GainMin, GainMax),
        Math.Clamp(Green, GainMin, GainMax),
        Math.Clamp(Blue, GainMin, GainMax),
        Math.Clamp(Gamma, GammaMin, GammaMax),
        Math.Clamp(Black, BlackMin, BlackMax),
        Math.Clamp(White, WhiteMin, WhiteMax));
}
