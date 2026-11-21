using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// The IR Loader in OmniControl's engine: the sound convolved with an impulse response file (zero added latency, <see cref="PartitionedConvolver"/>),
/// then Gain, then the effect's own Mix. The file is read, resampled, trimmed and levelled on a worker (<see cref="IrPrep"/>); until it is ready, and
/// when it is missing or unreadable, the sound passes unchanged (a missing file never silences the track). A stage that replaces another with the same
/// file and options carries on with its convolver, so changing Gain or Mix does not reload anything. Stereo only, like every stage of the pipe.
internal sealed class IrStage : IDspStage
{
    private const int Scratch = 4096;

    private sealed record Key(string Path, long Written, bool AutoLevel, bool Trim);
    private sealed record Kernel(Key Key, PartitionedConvolver? Left, PartitionedConvolver? Right);

    private readonly Key _key;
    private Glide _gain, _mix;
    private Kernel? _active;
    private Kernel? _ready;
    private readonly float[] _inL = new float[Scratch], _inR = new float[Scratch], _outL = new float[Scratch], _outR = new float[Scratch];

    /// <param name="values">The slot's values in the catalog's order (<see cref="IrParams"/>).</param>
    public IrStage(string path, IReadOnlyList<double> values)
    {
        _key = new Key(path, IrFileStamp.Of(path), IrParams.IsOn(values[IrParams.AutoLevel]), IrParams.IsOn(values[IrParams.TrimSilence]));
        _gain = new Glide((float)DspMath.DbToLin(values[IrParams.Gain]));
        _mix = new Glide((float)(values[IrParams.Mix] / Core.Models.Percent.Max));
        var key = _key;
        Guard.OffUi("Impulse response: loading", () => Volatile.Write(ref _ready, Load(key)));
    }

    /// Worker: the prepared impulse response; an empty kernel (pass-through) when there is no usable file. Every failure ends in a kernel,
    /// so a stage never waits for ever on the previous file; an unexpected one (a damaged file the reader trips on) is also logged.
    private static Kernel Load(Key key)
    {
        if (key.Path.Length == 0) return new Kernel(key, null, null);
        try
        {
            var prepared = IrPrep.Prepare(IrWaveFile.Read(key.Path), null, null, PipeFormat.SampleRate, key.AutoLevel, key.Trim);
            return new Kernel(key, new PartitionedConvolver(prepared.Left), ReferenceEquals(prepared.Left, prepared.Right) ? new PartitionedConvolver(prepared.Left) : new PartitionedConvolver(prepared.Right));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return new Kernel(key, null, null); // the file is missing or not a WAV this reader takes: the sound passes unchanged
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or NotSupportedException)
        {
            Shared.Services.Guard.Log($"Effects: loading the IR file {key.Path}", ex);
            return new Kernel(key, null, null);
        }
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != 2) return;
        if (Interlocked.Exchange(ref _ready, null) is { } next && next.Key != _active?.Key) _active = next;
        if (_active is not { Left: { } left, Right: { } right }) return;
        for (var at = 0; at < samples.Length; at += 2 * Scratch)
        {
            var part = samples.Slice(at, Math.Min(2 * Scratch, samples.Length - at));
            var frames = part.Length / 2;
            for (var f = 0; f < frames; f++) { _inL[f] = part[2 * f]; _inR[f] = part[2 * f + 1]; }
            left.Process(_inL.AsSpan(0, frames), _outL);
            right.Process(_inR.AsSpan(0, frames), _outR);
            for (var f = 0; f < frames; f++)
            {
                var gain = _gain.Next();
                var mix = _mix.Next();
                part[2 * f] = _inL[f] + (_outL[f] * gain - _inL[f]) * mix;
                part[2 * f + 1] = _inR[f] + (_outR[f] * gain - _inR[f]) * mix;
            }
        }
    }

    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is not IrStage old) return;
        _gain.StartFrom(old._gain.Now);
        _mix.StartFrom(old._mix.Now);
        _active ??= old._active; // until this stage's own file is ready the previous impulse response goes on, so an edit leaves no gap
    }
}
