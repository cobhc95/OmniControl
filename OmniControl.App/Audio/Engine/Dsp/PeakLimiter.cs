namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>
/// The last step of every program engine: keeps the output below <see cref="CeilingDb"/>, so EQ or gain boosts never reach full
/// scale and clip in the device's conversion (heard as crackle). Below the ceiling it changes nothing (gain exactly 1). A louder
/// peak is turned down at once, stereo-linked, and the level comes back over <see cref="Services.Timing.LimiterRelease"/>.
/// A non-finite sample (a broken effect) is replaced by silence. Audio thread only; no allocation.
/// </summary>
internal sealed class PeakLimiter
{
    /// <summary>The loudest the output may be, in dB below full scale (a small margin for the device's own conversion).</summary>
    public const double CeilingDb = -1;

    private static readonly float Ceiling = (float)Math.Pow(10, CeilingDb / 20);

    /// <summary>A recovering gain this close to 1 (under 0.0001 dB) is set to exactly 1: float steps would otherwise stop just short of it.</summary>
    private const float SettledGap = 1e-5f;

    private readonly float _recover; // share of the remaining gain reduction given back per frame
    private float _gain = 1;

    public PeakLimiter(int sampleRate) =>
        _recover = (float)(1 - Math.Exp(-1 / (Services.Timing.LimiterRelease.TotalSeconds * sampleRate)));

    public void Process(Span<float> samples, int channels)
    {
        for (var at = 0; at + channels <= samples.Length; at += channels)
        {
            var frame = samples.Slice(at, channels);
            var peak = 0f;
            for (var ch = 0; ch < channels; ch++)
            {
                if (!float.IsFinite(frame[ch])) frame[ch] = 0;
                peak = Math.Max(peak, Math.Abs(frame[ch]));
            }
            var gain = _gain + (1 - _gain) * _recover;
            if (1 - gain < SettledGap) gain = 1;
            if (peak * gain > Ceiling) gain = Ceiling / peak;
            _gain = gain;
            if (gain < 1) for (var ch = 0; ch < channels; ch++) frame[ch] *= gain;
        }
    }
}
