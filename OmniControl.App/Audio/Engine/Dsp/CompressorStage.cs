using OmniControl.Core.Models;

namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>
/// The compressor: a feed-forward peak compressor with a soft knee, stereo linked, attack and release in the gain-reduction domain
/// (one pole each, in dB), and a makeup gain (set by hand, or "auto": what brings a full-scale signal back to full scale). Ported from
/// TabForge's CompressorEffect (MIT, see NOTICE.txt); the gain computer is <see cref="CompressorCurve"/>, the same function the graph
/// draws. The two meters (gain reduction, input level) are written once per block and fall slowly.
/// </summary>
internal sealed class CompressorStage : IDspStage, IDynamicsReadout
{
    /// <summary>The meters: how fast they fall, the quietest input level they report and the most reduction (dB).</summary>
    private const double MeterFallMs = 300, MeterFloorDb = -100, MeterCeilingDb = 6, MaxReductionDb = 40;

    private readonly double _threshold, _ratio, _knee, _attack, _release;
    private readonly int _sampleRate;
    private Glide _makeup;
    private double _reduction, _reductionMeter, _levelMeter = MeterFloorDb;
    private double _shownReduction, _shownLevel = MeterFloorDb;

    /// <param name="values">The slot's values in the catalog's order (<see cref="CompressorParams"/>).</param>
    public CompressorStage(IReadOnlyList<double> values, int sampleRate)
    {
        _sampleRate = sampleRate;
        _threshold = values[CompressorParams.Threshold];
        _ratio = values[CompressorParams.Ratio];
        _attack = DspMath.Keep(values[CompressorParams.Attack], sampleRate);
        _release = DspMath.Keep(values[CompressorParams.Release], sampleRate);
        _knee = values[CompressorParams.Knee];
        var makeup = CompressorParams.IsOn(values[CompressorParams.AutoMakeup])
            ? Math.Min(CompressorParams.MaxMakeup, CompressorCurve.ReductionDb(0, _threshold, _ratio, _knee))
            : values[CompressorParams.Makeup];
        _makeup = new Glide((float)makeup);
    }

    public double ReductionDb => Volatile.Read(ref _shownReduction);
    public double LevelDb => Volatile.Read(ref _shownLevel);

    public void Process(Span<float> samples, int channels)
    {
        double blockPeak = 0, blockReduction = 0;
        for (var at = 0; at + channels <= samples.Length; at += channels)
        {
            var peak = 0f;
            for (var ch = 0; ch < channels; ch++) peak = Math.Max(peak, Math.Abs(samples[at + ch]));
            if (peak > blockPeak) blockPeak = peak;
            var wanted = CompressorCurve.ReductionDb(DspMath.LinToDb(peak), _threshold, _ratio, _knee);
            var keep = wanted > _reduction ? _attack : _release;
            _reduction = DspMath.Flush(wanted + keep * (_reduction - wanted));
            if (_reduction > blockReduction) blockReduction = _reduction;
            var gain = (float)DspMath.DbToLin(_makeup.Next() - _reduction);
            for (var ch = 0; ch < channels; ch++) samples[at + ch] *= gain;
        }
        var fall = Math.Exp(-1000.0 * (samples.Length / Math.Max(1, channels)) / (MeterFallMs * _sampleRate));
        _reductionMeter = Math.Max(blockReduction, _reductionMeter * fall);
        _levelMeter = Math.Max(DspMath.LinToDb(blockPeak), MeterFloorDb + (_levelMeter - MeterFloorDb) * fall);
        Volatile.Write(ref _shownReduction, Math.Min(MaxReductionDb, _reductionMeter));
        Volatile.Write(ref _shownLevel, Math.Clamp(_levelMeter, MeterFloorDb, MeterCeilingDb));
    }

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is not CompressorStage before) return;
        _reduction = before._reduction;
        _reductionMeter = before._reductionMeter;
        _levelMeter = before._levelMeter;
        _makeup.StartFrom(before._makeup.Now);
    }
}
