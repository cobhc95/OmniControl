using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OmniControl.Core.Engine;

/// <summary>
/// The effects, as the playback thread calls them: change <paramref name="samples"/> in place (interleaved floats). Called on the
/// audio thread for every block: must not allocate, lock or block.
/// </summary>
public interface IAudioBlockProcessor
{
    void Process(Span<float> samples, int channels, int sampleRate);
}

/// <summary>
/// Capture, effects, playback: records one or more sources (<see cref="PipeSource"/>) on their own threads, mixes them on the
/// playback thread of the first output device (each capture read through a <see cref="DriftReader"/> at its own gain,
/// <see cref="SetSourceGain"/>), runs the <see cref="IAudioBlockProcessor"/> and plays the result on that device and on every further
/// one (<see cref="ExtraOutput"/>: each keeps step with the first through the same drift correction). Nothing here touches
/// the original program's sound: silencing or routing it is the caller's job. Dispose stops everything.
/// </summary>
public sealed class AudioPipe : IDisposable
{
    private readonly List<PipeCapture> _captures;
    private readonly List<WasapiPlayer> _players;
    private readonly PipeOutput _output;
    private int _failed;

    /// <summary>Raised once, from an audio or worker thread, when a capture or a playback stopped with an error.</summary>
    public event Action<string>? Failed;

    private AudioPipe(List<PipeCapture> captures, List<WasapiPlayer> players, PipeOutput output)
    {
        _captures = captures;
        _players = players;
        _output = output;
        foreach (var c in captures) c.Failed += Fail;
        foreach (var p in players) p.PlaybackStopped += (_, e) => { if (e.Exception is { } ex) Fail(ex.Message); };
        output.Failed += Fail;
    }

    /// <summary>
    /// Starts the captures and the playback on every device of <paramref name="renderDeviceIds"/> (at least one; null = the default
    /// playback device). Throws <see cref="InvalidOperationException"/> with a reason when Windows refuses; nothing is left running then.
    /// </summary>
    public static async Task<AudioPipe> StartAsync(IReadOnlyList<PipeSource> sources, IReadOnlyList<string?> renderDeviceIds, IAudioBlockProcessor processor)
    {
        if (renderDeviceIds.Count == 0) throw new InvalidOperationException("There is no playback device to play on.");
        var captures = new List<PipeCapture>();
        var players = new List<WasapiPlayer>();
        try
        {
            foreach (var source in sources) captures.Add(await PipeCapture.StartAsync(source).ConfigureAwait(false));
            var extras = renderDeviceIds.Skip(1).Select(_ => new ExtraOutput()).ToList();
            var output = new PipeOutput(captures.Select(c => c.Ring).ToList(), extras, processor);
            using (var devices = new MMDeviceEnumerator())
            {
                for (var i = 0; i < renderDeviceIds.Count; i++)
                {
                    var player = Player(devices, renderDeviceIds[i]);
                    players.Add(player);
                    player.Init(i == 0 ? output : extras[i - 1]);
                }
            }
            var pipe = new AudioPipe(captures, players, output);
            foreach (var p in players) p.Play();
            return pipe;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            foreach (var p in players) p.Dispose();
            foreach (var c in captures) c.Dispose();
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static WasapiPlayer Player(MMDeviceEnumerator devices, string? deviceId) =>
        new WasapiPlayerBuilder()
            .WithDevice(deviceId is null ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(deviceId))
            .WithSharedMode()
            .WithLatency(CoreTiming.PipeRenderLatencyMs)
            .WithMmcssThreadPriority("Audio")
            .Build();

    /// <summary>The loudest raw captured sample (before effects) since the last call; 0 = silence.</summary>
    public float TakeCapturePeak()
    {
        var peak = 0f;
        foreach (var c in _captures) peak = Math.Max(peak, c.TakePeak());
        return peak;
    }

    /// <summary>
    /// The gain the mix gives source <paramref name="index"/> (in the order the sources were given; 1 = as recorded, 0 = muted).
    /// Any thread; the playback thread picks it up with the next block.
    /// </summary>
    /// <param name="rawCeiling">
    /// A block of this source whose recorded samples (before the gain) go above this is left out of the mix: a large make-up gain on a
    /// source that should arrive turned down (<c>Quiet</c> silencing) must never amplify full-level sound.
    /// </param>
    public void SetSourceGain(int index, float gain, float rawCeiling = float.PositiveInfinity) => _output.SetGain(index, gain, rawCeiling);

    /// <summary>
    /// Estimated extra delay of the processed sound on its slowest device: capture packet + what waits in the queue + the playback
    /// buffer, plus an extra device's own queue.
    /// </summary>
    public int DelayMs
    {
        get
        {
            var playback = _players[0].LatencyMilliseconds;
            for (var i = 0; i < _output.Extras.Count; i++)
                playback = Math.Max(playback, _output.Extras[i].QueuedMs + _players[i + 1].LatencyMilliseconds);
            return CoreTiming.PipeCaptureBufferMs + _output.QueuedMs + playback;
        }
    }

    /// <summary>
    /// Adds one source's block to the mix at <paramref name="gain"/>; false (nothing added) when the gain is 0 or a recorded sample goes
    /// above <paramref name="rawCeiling"/>. Playback thread: no allocation.
    /// </summary>
    internal static bool MixInto(Span<float> mix, ReadOnlySpan<float> part, float gain, float rawCeiling)
    {
        if (gain == 0f) return false;
        if (!float.IsPositiveInfinity(rawCeiling))
            foreach (var s in part)
                if (Math.Abs(s) > rawCeiling) return false;
        for (var k = 0; k < part.Length; k++) mix[k] += part[k] * gain;
        return true;
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0) Failed?.Invoke(reason);
    }

    public void Dispose()
    {
        foreach (var p in _players) p.Dispose();
        foreach (var c in _captures)
        {
            c.Failed -= Fail;
            c.Dispose();
        }
    }

    /// <summary>The first device's playback side: its playback thread pulls blocks from here and hands a copy to every extra device.</summary>
    private sealed class PipeOutput : IWaveProvider
    {
        private readonly DriftReader[] _sources;
        private readonly float[] _gains;
        private readonly float[] _ceilings;
        private readonly IAudioBlockProcessor _processor;
        private readonly float[] _block = new float[CoreTiming.PipeMaxBlockFrames * CoreTiming.PipeChannels];
        private readonly float[] _part = new float[CoreTiming.PipeMaxBlockFrames * CoreTiming.PipeChannels];
        private int _processorBroken;

        public event Action<string>? Failed;

        public PipeOutput(IReadOnlyList<AudioRing> rings, IReadOnlyList<ExtraOutput> extras, IAudioBlockProcessor processor)
        {
            // Each capture runs on its own clock: read through drift correction so the mix never trims or runs dry as the clocks part.
            _sources = rings.Select(r => new DriftReader(r, CoreTiming.PipeCaptureDriftTargetMs)).ToArray();
            _gains = Enumerable.Repeat(1f, rings.Count).ToArray();
            _ceilings = Enumerable.Repeat(float.PositiveInfinity, rings.Count).ToArray();
            Extras = extras;
            _processor = processor;
        }

        public IReadOnlyList<ExtraOutput> Extras { get; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CoreTiming.PipeSampleRate, CoreTiming.PipeChannels);

        public int QueuedMs => _sources.Length == 0 ? 0 : _sources.Max(s => s.QueuedMs);

        public void SetGain(int index, float gain, float rawCeiling)
        {
            if (index < 0 || index >= _gains.Length) return;
            // The playback thread reads both without a lock: through 0 first, so it never pairs a large gain with no ceiling.
            Volatile.Write(ref _gains[index], 0f);
            Volatile.Write(ref _ceilings[index], rawCeiling);
            Volatile.Write(ref _gains[index], gain);
        }

        public int Read(Span<byte> buffer)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer);
            var done = 0;
            while (done < output.Length)
            {
                var n = Math.Min(_block.Length, output.Length - done);
                var block = _block.AsSpan(0, n);
                Fill(block);
                Process(block);
                block.CopyTo(output[done..]);
                for (var i = 0; i < Extras.Count; i++) Extras[i].Queue.Write(block);
                done += n;
            }
            return done * sizeof(float);
        }

        /// <summary>Mixes what each capture has queued, each at its own gain (see <see cref="DriftReader"/> for priming and trimming).</summary>
        private void Fill(Span<float> block)
        {
            block.Clear();
            var part = _part.AsSpan(0, block.Length);
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].Read(part); // always read, also while muted, so the queue keeps its pace
                MixInto(block, part, Volatile.Read(ref _gains[i]), Volatile.Read(ref _ceilings[i]));
            }
        }

        /// <summary>Runs the effects; a processor that throws is left out from then on (the sound plays dry) and reported once.</summary>
        private void Process(Span<float> block)
        {
            if (Volatile.Read(ref _processorBroken) != 0) return;
            try
            {
                _processor.Process(block, CoreTiming.PipeChannels, CoreTiming.PipeSampleRate);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException or ObjectDisposedException or SEHException)
            {
                // An effect (usually a plug-in) failed on the audio thread: never let it stop the sound.
                Volatile.Write(ref _processorBroken, 1);
                var reason = ex.Message;
                ThreadPool.QueueUserWorkItem(_ => Failed?.Invoke($"An effect failed: {reason}"));
            }
        }
    }
}
