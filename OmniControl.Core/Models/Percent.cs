namespace OmniControl.Core.Models;

/// The 0-100 range of volumes, levels and wet mixes, and the conversions to and from raw device values, defined once.
public static class Percent
{
    public const int Min = 0, Max = 100;

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);

    /// The top of a level that may boost (a combined microphone's member: 0-200 %, 100 = as recorded).
    public const int BoostMax = 200;

    public static int ClampBoost(int value) => Math.Clamp(value, Min, BoostMax);

    /// A percent (clamped to 0-<see cref="BoostMax"/>) as a gain factor: 100 = 1.
    public static float ToGain(int percent) => ClampBoost(percent) / (float)Max;

    /// A raw value as a whole 0-100 share of its maximum (0 when the maximum is unknown); rounds down.
    public static int Of(long current, long maximum) => maximum <= 0 ? 0 : (int)(current * Max / maximum);

    /// Where <paramref name="value"/> sits in <paramref name="min"/>-<paramref name="max"/>, as a whole 0-100 share (what a level pop-up draws).
    public static int OfRange(int value, int min, int max) => Of(value - min, max - min);

    /// A whole 0-100 percent as a raw device value out of <paramref name="maximum"/>; rounds to the nearest.
    public static uint ToRaw(int percent, long maximum) => (uint)((Clamp(percent) * Math.Max(maximum, 0) + Max / 2) / Max);

    /// A 0-1 scalar (Core Audio) as a whole percent.
    public static int FromScalar(float scalar) => (int)Math.Round(scalar * Max);

    /// A percent (clamped to 0-100) as a 0-1 scalar.
    public static float ToScalar(int percent) => Clamp(percent) / (float)Max;

    /// <paramref name="percent"/> % of a range, at least 1: the one rule for how far a step moves a value (a wheel notch or a key is 1 % of
    /// the range, a large step 10 %).
    public static double StepOf(double minimum, double maximum, double percent = 1) => Math.Max(1, (maximum - minimum) * percent / Max);
}
