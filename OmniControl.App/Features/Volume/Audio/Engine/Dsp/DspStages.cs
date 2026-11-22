namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// One effect of a built chain. <see cref="Process"/> runs on the audio thread: no allocation, no lock. Everything a stage needs is
/// made when it is built (off the audio thread).
internal interface IDspStage
{
    void Process(Span<float> samples, int channels);

    /// Audio thread: carry on from the stage of the same effect in the chain this one replaces (same kind), so an edit does not
    /// click: filter state is kept and changed values glide from where the old stage was (<see cref="PipeFormat.GlideFrames"/>).
    void TakeStateFrom(IDspStage previous);

    /// How many frames late this stage's output is (only plug-ins delay the sound; it is added to the delay the row shows).
    int LatencyFrames => 0;

    /// The stage's live meters (the dynamics effects); null for the others. Read on the UI thread.
    IDynamicsReadout? Readout => null;

    /// Where the stage copies the sound it receives for a live spectrum (recording only while enabled); null for the effects that have no graph.
    SpectrumTap? Tap => null;
}

/// A value that moves to its target in a straight line over <see cref="PipeFormat.GlideFrames"/>, one step per frame (audio thread).
internal struct Glide
{
    private float _step;
    private int _left;

    public Glide(float value)
    {
        Target = value;
        Now = value;
    }

    public float Target { get; }
    public float Now { get; private set; }

    /// Starts from where a replaced stage was.
    public void StartFrom(float from)
    {
        if (from == Target) return;
        Now = from;
        _left = PipeFormat.GlideFrames;
        _step = (Target - from) / _left;
    }

    public bool Moving => _left > 0;

    /// Multiplies interleaved <paramref name="samples"/> by the value, gliding frame by frame while it moves (a settled 1 leaves them alone).
    public void Apply(Span<float> samples, int channels)
    {
        if (!Moving)
        {
            var gain = Now;
            if (gain == 1) return;
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
            return;
        }
        for (var at = 0; at + channels <= samples.Length; at += channels)
        {
            var gain = Next();
            for (var ch = 0; ch < channels; ch++) samples[at + ch] *= gain;
        }
    }

    /// The value for the next frame.
    public float Next()
    {
        if (_left == 0) return Now;
        Now = --_left == 0 ? Target : Now + _step;
        return Now;
    }
}

/// A gain in dB.
internal sealed class GainStage : IDspStage
{
    private Glide _gain;

    public GainStage(double db) => _gain = new Glide((float)Math.Pow(10, db / 20));

    public void Process(Span<float> samples, int channels) => _gain.Apply(samples, channels);

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is GainStage gain) _gain.StartFrom(gain._gain.Now);
    }
}

/// A plug-in effect (any format) on the stereo pipe. A mono plug-in hears left and right mixed, and its one output goes to both sides.
/// The effect object belongs to <see cref="PluginChainHost"/>, which keeps it alive while a chain uses it.
internal sealed class PluginStage : IDspStage
{
    private readonly IPluginEffect _effect;
    private readonly float[] _input;
    private readonly float[] _output;

    public PluginStage(IPluginEffect effect)
    {
        _effect = effect;
        _input = new float[effect.MaxBlockFrames * effect.InputChannels];
        _output = new float[effect.MaxBlockFrames * effect.OutputChannels];
    }

    public int LatencyFrames => _effect.LatencyFrames;

    public void Process(Span<float> samples, int channels)
    {
        if (channels != 2) return;
        var max = _effect.MaxBlockFrames * 2;
        for (var at = 0; at < samples.Length; at += max)
        {
            var part = samples.Slice(at, Math.Min(max, samples.Length - at));
            var frames = part.Length / 2;
            if (_effect.InputChannels == 2) part.CopyTo(_input);
            else for (var f = 0; f < frames; f++) _input[f] = (part[2 * f] + part[2 * f + 1]) * 0.5f;

            _effect.Process(_input.AsSpan(0, frames * _effect.InputChannels), _output, frames);

            if (_effect.OutputChannels == 2) _output.AsSpan(0, part.Length).CopyTo(part);
            else for (var f = 0; f < frames; f++) part[2 * f] = part[2 * f + 1] = _output[f];
        }
    }

    public void TakeStateFrom(IDspStage previous) { }
}

/// Mixes a stage's result with the dry sound (the slot's Wet percent). Every effect is wrapped in one, so moving Wet across 100 %
/// keeps the effect's state; at a settled 100 % only the effect runs (the sound is exactly the effect's own).
internal sealed class WetStage : IDspStage
{
    private readonly IDspStage _inner;
    private readonly float[] _dry;
    private Glide _wet;

    public WetStage(IDspStage inner, double wet, int maxSamples)
    {
        _inner = inner;
        _wet = new Glide((float)wet);
        _dry = new float[maxSamples];
    }

    public IDspStage Inner => _inner;
    public int LatencyFrames => _inner.LatencyFrames;
    public IDynamicsReadout? Readout => _inner.Readout;
    public SpectrumTap? Tap => _inner.Tap;

    public void Process(Span<float> samples, int channels)
    {
        if (!_wet.Moving && _wet.Now == 1)
        {
            _inner.Process(samples, channels);
            return;
        }
        var dry = _dry.AsSpan(0, Math.Min(samples.Length, _dry.Length));
        samples[..dry.Length].CopyTo(dry);
        _inner.Process(samples, channels);
        for (var at = 0; at + channels <= dry.Length; at += channels)
        {
            var wet = _wet.Next();
            for (var i = at; i < at + channels; i++) samples[i] = dry[i] + (samples[i] - dry[i]) * wet;
        }
    }

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is not WetStage wet) return;
        _wet.StartFrom(wet._wet.Now);
        if (_inner.GetType() == wet._inner.GetType()) _inner.TakeStateFrom(wet._inner);
    }
}

/// The dry sound a folder's <see cref="MixOpenStage"/> keeps for its <see cref="MixCloseStage"/> (one block; audio thread).
internal sealed class MixTap
{
    public readonly float[] Dry = new float[PipeFormat.MaxBlockSamples];
    public int Length;
}

/// Start of a device folder's effects: keeps the dry sound so the folder's Mix can blend it back at <see cref="MixCloseStage"/>.
internal sealed class MixOpenStage : IDspStage
{
    private readonly MixTap _tap;

    public MixOpenStage(MixTap tap) => _tap = tap;

    public void Process(Span<float> samples, int channels)
    {
        _tap.Length = Math.Min(samples.Length, _tap.Dry.Length);
        samples[.._tap.Length].CopyTo(_tap.Dry);
    }

    public void TakeStateFrom(IDspStage previous) { }
}

/// End of a device folder's effects: the folder's Mix (0-100 %) of their result with the dry sound; at a settled 100 % the sound passes untouched.
internal sealed class MixCloseStage : IDspStage
{
    private readonly MixTap _tap;
    private Glide _wet;

    public MixCloseStage(MixTap tap, double mix)
    {
        _tap = tap;
        _wet = new Glide((float)mix);
    }

    public void Process(Span<float> samples, int channels)
    {
        if (!_wet.Moving && _wet.Now == 1) return;
        for (var at = 0; at + channels <= _tap.Length && at + channels <= samples.Length; at += channels)
        {
            var wet = _wet.Next();
            for (var i = at; i < at + channels; i++) samples[i] = _tap.Dry[i] + (samples[i] - _tap.Dry[i]) * wet;
        }
    }

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is MixCloseStage close) _wet.StartFrom(close._wet.Now);
    }
}
