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
/// playback thread of the first output device, runs the <see cref="IAudioBlockProcessor"/> and plays the result on that device and
/// on every further one (<see cref="ExtraOutput"/>: each keeps step with the first through drift correction). Nothing here touches
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

    /// <summary>How many capture packets have arrived in total.</summary>
    public long CapturedPackets => _captures.Sum(c => c.Packets);

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
        private readonly IReadOnlyList<AudioRing> _rings;
        private readonly IAudioBlockProcessor _processor;
        private readonly bool[] _primed;
        private readonly float[] _block = new float[CoreTiming.PipeMaxBlockFrames * CoreTiming.PipeChannels];
        private int _processorBroken;

        private const int SamplesPerMs = CoreTiming.PipeSamplesPerMs;

        public event Action<string>? Failed;

        public PipeOutput(IReadOnlyList<AudioRing> rings, IReadOnlyList<ExtraOutput> extras, IAudioBlockProcessor processor)
        {
            _rings = rings;
            Extras = extras;
            _processor = processor;
            _primed = new bool[rings.Count];
        }

        public IReadOnlyList<ExtraOutput> Extras { get; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CoreTiming.PipeSampleRate, CoreTiming.PipeChannels);

        public int QueuedMs => _rings.Count == 0 ? 0 : _rings.Max(r => r.Count) / SamplesPerMs;

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

        /// <summary>Mixes what each capture has queued; a capture waits until it has the prefill, and a queue that grew too long is trimmed.</summary>
        private void Fill(Span<float> block)
        {
            block.Clear();
            for (var i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                if (ring.Count > CoreTiming.PipeMaxQueueMs * SamplesPerMs) ring.DropTo(CoreTiming.PipePrefillMs * SamplesPerMs);
                if (!_primed[i] && ring.Count < CoreTiming.PipePrefillMs * SamplesPerMs) continue;
                _primed[i] = ring.MixInto(block) == block.Length; // ran dry: gather the prefill again before playing on
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
