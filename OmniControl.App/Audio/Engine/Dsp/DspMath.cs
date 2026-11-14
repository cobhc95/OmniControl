namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>The small maths the dynamics stages share: dB and linear amplitude, one-pole time constants, denormal flushing (from TabForge's DspMath, MIT, see NOTICE.txt).</summary>
internal static class DspMath
{
    /// <summary>Below this magnitude an envelope state is set to zero: denormal numbers make the audio thread slow.</summary>
    private const double DenormalFloor = 1e-20;

    /// <summary>The quietest level a meter or a level-to-dB conversion tells apart (silence reads this, never minus infinity).</summary>
    private const double FloorLinear = 1e-9;

    public static double DbToLin(double db) => Math.Exp(db * 0.115129254649702284); // ln(10) / 20

    public static double LinToDb(double lin) => 20 * Math.Log10(Math.Max(lin, FloorLinear));

    public static double Flush(double x) => Math.Abs(x) < DenormalFloor ? 0 : x;

    /// <summary>The "keep" factor of a one-pole follower with time constant <paramref name="ms"/> (63 % of the way there in that time): y = target + keep * (y - target).</summary>
    public static double Keep(double ms, double sampleRate) => ms <= 0 ? 0 : Math.Exp(-1000.0 / (ms * sampleRate));
}

/// <summary>What a running dynamics stage (compressor, limiter) reports for the FX window's meters; written once per block on the audio thread, read on the UI thread.</summary>
internal interface IDynamicsReadout
{
    /// <summary>How far the stage turns the sound down now, in dB (0 or more; falls slowly).</summary>
    double ReductionDb { get; }

    /// <summary>The level the stage's meter shows, in dBFS (the compressor's input, the limiter's output; falls slowly).</summary>
    double LevelDb { get; }
}
