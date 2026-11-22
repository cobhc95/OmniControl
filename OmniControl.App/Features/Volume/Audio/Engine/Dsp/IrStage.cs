using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// The IR Loader in OmniControl's engine: the sound convolved with an impulse response file (zero added latency, <see cref="PartitionedConvolver"/>),
/// then Gain, then the effect's own Mix. The file is read, resampled, trimmed and levelled on a worker (<see cref="IrPrep"/>); until it is ready, and
/// when it is missing or unreadable, the sound passes unchanged (a missing file never silences the track). A stage that replaces another with the same
/// file and options shares its load and carries on with its convolver, so changing Gain or Mix reads nothing. Stereo only, like every stage of the pipe.
internal sealed class IrStage : IDspStage
{
    private const int Scratch = 4096;

    internal sealed record Key(string Path, long Written, bool AutoLevel, bool Trim);
    internal sealed record Kernel(Key Key, PartitionedConvolver? Left, PartitionedConvolver? Right);

    /// One requested load of a file version, shared by every successive stage of the slot that keeps the same key; a newer stage with
    /// another key stops it, so rapid file changes read only the latest file and the worker holds no stage.
    internal sealed class Load
    {
        private volatile bool _stopped;
        private Kernel? _kernel;

        public Load(Key key) => Key = key;

        public Key Key { get; }

        /// The prepared kernel once the worker is done; null while it works or when it was stopped.
        public Kernel? Kernel => Volatile.Read(ref _kernel);

        public void Stop() => _stopped = true;

        /// Worker (or the measurement's own thread).
        public void Run()
        {
            if (Prepare(Key, () => _stopped) is { } kernel) Volatile.Write(ref _kernel, kernel);
        }
    }

    private readonly Load _load;
    private Glide _gain, _mix;
    private Kernel? _active;
    private readonly float[] _inL = new float[Scratch], _inR = new float[Scratch], _outL = new float[Scratch], _outR = new float[Scratch];

    /// <param name="values">The slot's values in the catalog's order (<see cref="IrParams"/>).</param>
    /// <param name="previous">The stage of the same slot in the chain this one replaces: with the same file and options its load is taken, not repeated.</param>
    /// <param name="loadHere">Read the file now on this thread (a measurement, which must not hear the pass-through), not on a worker.</param>
    public IrStage(string path, IReadOnlyList<double> values, IrStage? previous = null, bool loadHere = false)
    {
        var key = new Key(path, IrFileStamp.Of(path), IrParams.IsOn(values[IrParams.AutoLevel]), IrParams.IsOn(values[IrParams.TrimSilence]));
        _gain = new Glide((float)DspMath.DbToLin(values[IrParams.Gain]));
        _mix = new Glide((float)(values[IrParams.Mix] / Core.Models.Percent.Max));
        if (previous?._load is { } shared && shared.Key == key)
        {
            _load = shared;
            return;
        }
        previous?._load.Stop(); // superseded: only the latest requested file is prepared
        _load = new Load(key);
        if (loadHere) _load.Run();
        else Guard.OffUi("Impulse response: loading", _load.Run);
    }

    internal Load Loading => _load;

    /// Worker: the prepared impulse response; an empty kernel (pass-through) when there is no usable file; null when <paramref name="stopped"/>
    /// said a newer file was asked for. Every failure ends in a kernel, so a stage never waits for ever on the previous file; an unexpected one
    /// (a damaged file the reader trips on) is also logged.
    private static Kernel? Prepare(Key key, Func<bool> stopped)
    {
        if (key.Path.Length == 0) return new Kernel(key, null, null);
        try
        {
            if (stopped()) return null;
            var audio = IrWaveFile.Read(key.Path);
            if (stopped()) return null;
            var prepared = IrPrep.Prepare(audio, null, null, PipeFormat.SampleRate, key.AutoLevel, key.Trim);
            if (stopped()) return null;
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
        if (_load.Kernel is { } next && !ReferenceEquals(next, _active)) _active = next;
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
