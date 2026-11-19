using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OmniControl.Core.Engine.Screenshot;

/// A recording's sound: what the speakers play (WASAPI loopback of the default output) and the default microphone, each switched on and
/// off at any moment, brought to 48 kHz stereo and mixed. Silence fills any gap (loopback sends nothing while nothing plays), and sound
/// that waited longer than <see cref="CoreTiming.RecordSoundMaxQueueMs"/> is dropped, so the sound never drifts behind the picture.
/// Each capture is opened only while it is on (so Windows' microphone sign shows only then) and released when it goes off.
public sealed class RecordingSound : IDisposable
{
    public const int SampleRate = 48000, Channels = 2, BytesPerFrame = Channels * 2;

    private readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };
    private readonly object _gate = new();
    private Source? _system, _microphone;
    private float[] _floats = Array.Empty<float>();

    /// Starts or stops recording what the speakers play. Null when done, otherwise why not.
    public string? SetSystem(bool on) => Set(ref _system, on, () => new WasapiLoopbackCapture(), "System sound");

    /// Starts or stops recording the default microphone. Null when done, otherwise why not.
    public string? SetMicrophone(bool on) => Set(ref _microphone, on, () => new WasapiCapture(), "The microphone");

    private string? Set(ref Source? source, bool on, Func<WasapiCapture> open, string what)
    {
        lock (_gate)
        {
            if (on == (source is not null)) return null;
            if (!on)
            {
                _mixer.RemoveMixerInput(source!.Output);
                source.Dispose();
                source = null;
                return null;
            }
            Source? made = null;
            try
            {
                made = new Source(open);
                _mixer.AddMixerInput(made.Output);
                made.Start();
                source = made;
                return null;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                if (made is not null)
                {
                    _mixer.RemoveMixerInput(made.Output);
                    made.Dispose();
                }
                return $"{what} could not be recorded: {ex.Message}";
            }
        }
    }

    /// Drops whatever sound is waiting (after a pause, so nothing from the pause is recorded).
    public void Clear()
    {
        lock (_gate)
        {
            _system?.Clear();
            _microphone?.Clear();
        }
    }

    /// Fills <paramref name="pcm"/> with the mixed sound as 16-bit stereo, silence where there is none.
    public void Read(Span<byte> pcm)
    {
        var samples = pcm.Length / 2;
        if (_floats.Length < samples) _floats = new float[samples];
        lock (_gate)
        {
            _system?.Trim();
            _microphone?.Trim();
        }
        var floats = _floats.AsSpan(0, samples);
        var read = _mixer.Read(floats);
        floats[read..].Clear();
        for (var i = 0; i < samples; i++)
        {
            var value = (short)Math.Clamp(floats[i] * short.MaxValue, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)value;
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _system?.Dispose();
            _microphone?.Dispose();
            _system = _microphone = null;
            _mixer.RemoveAllMixerInputs();
        }
    }

    /// One capture: its packets go into a buffer that is read back as 48 kHz stereo.
    private sealed class Source : IDisposable
    {
        private readonly WasapiCapture _capture;
        private readonly BufferedWaveProvider _buffer;
        private readonly int _maxBytes, _keepBytes;
        private byte[] _scratch = Array.Empty<byte>();

        public ISampleProvider Output { get; }

        /// Opens the capture; on a failure it is released before the exception goes on.
        public Source(Func<WasapiCapture> open)
        {
            _capture = open();
            try
            {
                var format = _capture.WaveFormat;
                _buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(CoreTiming.RecordSoundBufferMs))
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = true,
                };
                _maxBytes = BytesFor(format, CoreTiming.RecordSoundMaxQueueMs);
                _keepBytes = BytesFor(format, CoreTiming.RecordSoundLagMs);
                var output = _buffer.ToSampleProvider();
                if (format.Channels == 1) output = new MonoToStereoSampleProvider(output);
                else if (format.Channels > Channels) output = new MultiplexingSampleProvider(new[] { output }, Channels); // front left and right
                if (format.SampleRate != SampleRate) output = new WdlResamplingSampleProvider(output, SampleRate);
                Output = output;
            }
            catch
            {
                _capture.Dispose();
                throw;
            }
            _capture.DataAvailable += OnData;
        }

        public void Start() => _capture.StartRecording();

        private static int BytesFor(WaveFormat format, int ms) => format.AverageBytesPerSecond / 1000 * ms / format.BlockAlign * format.BlockAlign;

        private void OnData(object? sender, WaveInEventArgs e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        /// When too much waits, keeps only the newest <see cref="CoreTiming.RecordSoundLagMs"/>.
        public void Trim()
        {
            var extra = _buffer.BufferedBytes - _keepBytes;
            if (_buffer.BufferedBytes <= _maxBytes || extra <= 0) return;
            if (_scratch.Length < extra) _scratch = new byte[extra];
            _buffer.Read(_scratch.AsSpan(0, extra));
        }

        public void Clear() => _buffer.ClearBuffer();

        public void Dispose()
        {
            _capture.DataAvailable -= OnData;
            _capture.Dispose(); // stops the capture and releases its audio client
        }
    }
}
