namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// The look-ahead true-peak limiter. The input is delayed by the look-ahead D. Every sample the true peak gives the gain that would bring
/// it to the ceiling; the smallest gain over the last D - Delay + 2 samples is taken, released with a one-pole (a fixed time, or an
/// automatic one) and averaged over D - Delay + 1 samples, so the gain arrives at its value before the peak leaves the delay and the
/// output never passes the ceiling. Ported from TabForge's LimiterEffect (MIT, see NOTICE.txt). The look-ahead is reported as latency.
/// All that must carry on when a setting changes (the delay, the detectors, the averages) is <see cref="LimiterState"/>, handed on whole.
/// Stereo only, like every stage of the pipe.
internal sealed class LimiterStage : IDspStage, IDynamicsReadout
{
    private const double MeterFallMs = 500, SustainMs = 600, AutoFastMs = 20, AutoSlowMs = 400, SustainedBelow = 0.98;

    /// Aims a little under the ceiling: the 4x detector can miss a fraction of a dB between its points.
    private const double MarginDb = 0.1;

    private const double MeterFloorDb = -100, MeterCeilingDb = 6, MaxReductionDb = 30;

    private readonly double _ceiling, _fixedRelease, _sustainStep;
    private readonly bool _auto;
    private readonly int _sampleRate;
    private Glide _inputGain;
    private LimiterState _s;
    private double _shownReduction, _shownLevel = MeterFloorDb;

    /// <param name="values">The slot's values in the catalog's order (<see cref="LimiterParams"/>).</param>
    public LimiterStage(IReadOnlyList<double> values, int sampleRate)
    {
        _sampleRate = sampleRate;
        _ceiling = DspMath.DbToLin(values[LimiterParams.Ceiling] - MarginDb);
        _fixedRelease = 1 - DspMath.Keep(values[LimiterParams.Release], sampleRate);
        _sustainStep = 1 - DspMath.Keep(SustainMs, sampleRate);
        _auto = LimiterParams.IsOn(values[LimiterParams.AutoRelease]);
        _inputGain = new Glide((float)DspMath.DbToLin(values[LimiterParams.InputGain]));
        _s = new LimiterState(LimiterParams.LookAheadFrames(sampleRate));
    }

    public int LatencyFrames => _s.LookAhead;
    public double ReductionDb => Volatile.Read(ref _shownReduction);
    public double LevelDb => Volatile.Read(ref _shownLevel);

    public void Process(Span<float> samples, int channels)
    {
        if (channels != 2) return;
        var s = _s;
        var average = s.Average.Length;
        double lowestGain = 1, blockPeak = 0;
        for (var at = 0; at + 2 <= samples.Length; at += 2)
        {
            var gainIn = _inputGain.Next();
            var left = samples[at] * gainIn;
            var right = samples[at + 1] * gainIn;
            var peak = Math.Max(s.PeakLeft.Push(left), s.PeakRight.Push(right));
            var wanted = s.Minimum.Push(peak > _ceiling ? _ceiling / peak : 1.0);
            var releaseStep = _fixedRelease;
            if (_auto)
            {
                s.Sustain += ((s.Release < SustainedBelow ? 1 : 0) - s.Sustain) * _sustainStep;
                releaseStep = 1 - DspMath.Keep(AutoFastMs + (AutoSlowMs - AutoFastMs) * s.Sustain, _sampleRate);
            }
            s.Release = wanted < s.Release ? wanted : s.Release + (wanted - s.Release) * releaseStep;
            s.Sum += s.Release - s.Average[s.AverageAt];
            s.Average[s.AverageAt] = s.Release;
            if (++s.AverageAt == average) s.AverageAt = 0;
            var gain = s.Sum / average;
            if (gain < lowestGain) lowestGain = gain;
            double outLeft = s.DelayLeft[s.DelayAt] * gain, outRight = s.DelayRight[s.DelayAt] * gain;
            s.DelayLeft[s.DelayAt] = (float)left;
            s.DelayRight[s.DelayAt] = (float)right;
            if (++s.DelayAt == s.LookAhead) s.DelayAt = 0;
            samples[at] = (float)outLeft;
            samples[at + 1] = (float)outRight;
            blockPeak = Math.Max(blockPeak, Math.Max(Math.Abs(outLeft), Math.Abs(outRight)));
        }
        var fall = Math.Exp(-1000.0 * (samples.Length / 2) / (MeterFallMs * _sampleRate));
        s.ReductionMeter = Math.Max(-DspMath.LinToDb(lowestGain), s.ReductionMeter * fall);
        s.LevelMeter = Math.Max(DspMath.LinToDb(blockPeak), MeterFloorDb + (s.LevelMeter - MeterFloorDb) * fall);
        Volatile.Write(ref _shownReduction, Math.Min(MaxReductionDb, s.ReductionMeter));
        Volatile.Write(ref _shownLevel, Math.Clamp(s.LevelMeter, MeterFloorDb, MeterCeilingDb));
    }

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is not LimiterStage before) return;
        _s = before._s;
        _inputGain.StartFrom(before._inputGain.Now);
    }
}

/// What a limiter keeps from one block to the next: the delayed sound, the true-peak detectors, the window minimum, the gain averages and the meters. Audio thread only.
internal sealed class LimiterState
{
    private const double MeterFloorDb = -100;

    public LimiterState(int lookAhead)
    {
        LookAhead = lookAhead;
        DelayLeft = new float[lookAhead];
        DelayRight = new float[lookAhead];
        Average = new double[lookAhead - TruePeakDetector.Delay + 1];
        Array.Fill(Average, 1.0);
        Sum = Average.Length;
        Minimum = new SlidingMinimum(lookAhead - TruePeakDetector.Delay + 2);
        LevelMeter = MeterFloorDb;
    }

    public int LookAhead { get; }
    public float[] DelayLeft { get; }
    public float[] DelayRight { get; }
    public double[] Average { get; }
    public SlidingMinimum Minimum { get; }
    public TruePeakDetector PeakLeft { get; } = new();
    public TruePeakDetector PeakRight { get; } = new();
    public double Sum, Release = 1, Sustain, ReductionMeter, LevelMeter;
    public int DelayAt, AverageAt;
}
