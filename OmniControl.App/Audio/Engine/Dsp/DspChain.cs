using OmniControl.Core;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>A built chain: the stages to run in order, and what could not run here (shown to the user, never hidden).</summary>
internal sealed class DspChain
{
    private readonly IDspStage[] _stages;
    private readonly object[] _sources;

    /// <param name="sources">What each stage was built from (its slot), in the same order: a stage carries on from the stage of the same slot.</param>
    public DspChain(IDspStage[] stages, object[] sources, IReadOnlyList<string> skipped)
    {
        _stages = stages;
        _sources = sources;
        Skipped = skipped;
    }

    public static DspChain Empty { get; } = new(Array.Empty<IDspStage>(), Array.Empty<object>(), Array.Empty<string>());

    /// <summary>Names of effects kept in the chain that this engine does not run (with the reason).</summary>
    public IReadOnlyList<string> Skipped { get; }

    public int StageCount => _stages.Length;

    /// <summary>How many frames the stages delay the sound together (read live: a plug-in may change its latency while it runs).</summary>
    public int LatencyFrames
    {
        get
        {
            var frames = 0;
            foreach (var stage in _stages) frames += stage.LatencyFrames;
            return frames;
        }
    }

    public void Process(Span<float> samples, int channels)
    {
        foreach (var stage in _stages) stage.Process(samples, channels);
    }

    /// <summary>
    /// Audio thread: each stage carries on from the stage built from the same slot in the chain this one replaces (when they are the
    /// same kind), so moving or adding an effect does not hand one effect's state to another.
    /// </summary>
    public void TakeStateFrom(DspChain previous)
    {
        for (var i = 0; i < _stages.Length; i++)
        for (var j = 0; j < previous._stages.Length; j++)
        {
            if (!ReferenceEquals(_sources[i], previous._sources[j]) || _stages[i].GetType() != previous._stages[j].GetType()) continue;
            _stages[i].TakeStateFrom(previous._stages[j]);
            break;
        }
    }
}

/// <summary>
/// What an <see cref="AudioPipe"/> calls for every block: a closed gate gives silence (before the original program is silenced),
/// then the current chain, then the user's own volume, pan and mute for the row (when the original's session is muted, the row's
/// sliders act here instead), then the <see cref="PeakLimiter"/> so boosts never clip in the device. Changes from the UI thread are
/// swapped in whole: the audio thread only reads references.
/// </summary>
internal sealed class ChainProcessor : IAudioBlockProcessor
{
    private DspChain _chain = DspChain.Empty;
    private DspChain _running = DspChain.Empty;
    private readonly UserLevel? _level;
    private readonly PeakLimiter _limiter = new(PipeFormat.SampleRate);
    private volatile bool _gateOpen;

    /// <param name="level">The row's volume, pan and mute applied after the chain; null when the original's own session still applies them.</param>
    public ChainProcessor(UserLevel? level, bool gateOpen)
    {
        _level = level;
        _gateOpen = gateOpen;
    }

    /// <summary>UI thread: the chain to run from the next block on.</summary>
    public void SetChain(DspChain chain) => Volatile.Write(ref _chain, chain);

    public bool GateOpen
    {
        get => _gateOpen;
        set => _gateOpen = value;
    }

    public void Process(Span<float> samples, int channels, int sampleRate)
    {
        var chain = Volatile.Read(ref _chain);
        if (!ReferenceEquals(chain, _running))
        {
            chain.TakeStateFrom(_running);
            _running = chain;
        }
        if (!_gateOpen)
        {
            samples.Clear();
            return;
        }
        chain.Process(samples, channels);
        _level?.Apply(samples, channels);
        _limiter.Process(samples, channels);
    }
}

/// <summary>A row's volume, pan and mute as a gain per channel, eased over one block so a slider move never clicks.</summary>
internal sealed class UserLevel
{
    private float _targetLeft = 1, _targetRight = 1, _left = 1, _right = 1;

    /// <summary>UI thread: the row's values (volume 0-100, pan -100..100).</summary>
    public void Set(int volume, int pan, bool muted)
    {
        var gain = muted ? 0 : Core.Models.Percent.ToScalar(volume);
        PanMath.ToLevels(pan, out var l, out var r);
        Volatile.Write(ref _targetLeft, gain * l);
        Volatile.Write(ref _targetRight, gain * r);
    }

    public void Apply(Span<float> samples, int channels)
    {
        float toL = Volatile.Read(ref _targetLeft), toR = Volatile.Read(ref _targetRight);
        var frames = samples.Length / channels;
        if (frames == 0) return;
        float stepL = (toL - _left) / frames, stepR = (toR - _right) / frames;
        for (var f = 0; f < frames; f++)
        {
            _left += stepL;
            _right += stepR;
            var at = f * channels;
            samples[at] *= _left;
            for (var ch = 1; ch < channels; ch++) samples[at + ch] *= ch == 1 ? _right : (_left + _right) / 2;
        }
        _left = toL;
        _right = toR;
    }
}

/// <summary>The pipe's format, as the chain is built for it.</summary>
internal static class PipeFormat
{
    public const int SampleRate = CoreTiming.PipeSampleRate, Channels = CoreTiming.PipeChannels;
    public const int MaxBlockSamples = CoreTiming.PipeMaxBlockFrames * CoreTiming.PipeChannels;

    /// <summary>How many frames a changed effect value takes to reach its new value (<see cref="Services.Timing.EffectGlide"/>).</summary>
    public static readonly int GlideFrames = Math.Max(1, (int)(Services.Timing.EffectGlide.TotalSeconds * SampleRate));

    /// <summary>A number of frames as whole milliseconds.</summary>
    public static int ToMs(int frames) => (int)((long)frames * 1000 / SampleRate);
}
