namespace OmniControl.App.Features.Volume.Audio;

/// Where each setting of the Compressor sits in its slot's <see cref="FxSlot.Values"/> (the order of its <see cref="FxCatalog"/> entry:
/// stored in mixer.json, so new settings are only ever appended) and the limits both the catalog and the stage use. Names and ranges follow TabForge.
public static class CompressorParams
{
    public const int Threshold = 0, Ratio = 1, Attack = 2, Release = 3, Makeup = 4, Knee = 5, AutoMakeup = 6;
    public const double MinThreshold = -60, MaxRatio = 20, MaxKnee = 24, MaxMakeup = 24;

    /// A switch is stored as 0 or 1.
    public static bool IsOn(double value) => value >= 0.5;
}

/// Where each setting of the Limiter sits in its slot's values, and its fixed look-ahead (see <see cref="CompressorParams"/>).
public static class LimiterParams
{
    public const int Ceiling = 0, Release = 1, InputGain = 2, AutoRelease = 3;

    /// The look-ahead the limiter delays the sound by, in ms (reported as the effect's latency).
    public const double LookAheadMs = 1.5;

    public static bool IsOn(double value) => value >= 0.5;

    /// The least look-ahead in frames (a very low sample rate still gets room for the true-peak detector).
    private const int MinLookAheadFrames = 16;

    /// The look-ahead in frames at a sample rate: <see cref="LookAheadMs"/>, at least <see cref="MinLookAheadFrames"/>.
    public static int LookAheadFrames(int sampleRate) => Math.Max(MinLookAheadFrames, (int)Math.Round(LookAheadMs * sampleRate / 1000));
}
