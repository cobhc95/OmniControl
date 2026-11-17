using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OmniControl.Core.Engine;

/// <summary>
/// Where an effects pipe takes its sound from: one program's process tree, everything played on one playback device (a cable's
/// playing side, by loopback), or a recording device (a microphone, or a cable's recording side).
/// </summary>
public sealed record PipeSource(int ProcessId, string? DeviceId, bool Loopback)
{
    public static PipeSource Program(int processId) => new(processId, null, false);
    public static PipeSource Playback(string deviceId) => new(0, deviceId, true);
    public static PipeSource Recording(string deviceId) => new(0, deviceId, false);
}

/// <summary>
/// One capture of an effects pipe: records its source in the pipe's format on its own thread and pushes the samples into a ring
/// for the playback thread. Also keeps the loudest raw sample seen, so the engine can tell whether the capture went silent.
/// </summary>
internal sealed class PipeCapture : IDisposable
{
    private readonly WasapiRecorder _recorder;
    private readonly bool _pcm16;
    private readonly float[] _scratch;
    private int _peakBits;

    public AudioRing Ring { get; }

    /// <summary>Raised (on the capture thread) when the capture stopped with an error, e.g. the program closed or the device went away.</summary>
    public event Action<string>? Failed;

    private PipeCapture(WasapiRecorder recorder)
    {
        _recorder = recorder;
        _pcm16 = recorder.WaveFormat.Encoding == WaveFormatEncoding.Pcm;
        var samplesPerPacketMax = CoreTiming.PipeSamplesPerMs * CoreTiming.PipeCaptureBufferMs * 8;
        _scratch = new float[samplesPerPacketMax];
        Ring = new AudioRing(CoreTiming.PipeRingSamples);
        _recorder.DataAvailable += OnData;
        _recorder.RecordingStopped += (_, e) => { if (e.Exception is { } ex) Failed?.Invoke(ex.Message); };
    }

    /// <summary>Formats asked for in order: float first (no conversion), then 16-bit PCM like Microsoft's process loopback sample.</summary>
    private static readonly WaveFormat[] Formats =
    {
        WaveFormat.CreateIeeeFloatWaveFormat(CoreTiming.PipeSampleRate, CoreTiming.PipeChannels),
        new(CoreTiming.PipeSampleRate, 16, CoreTiming.PipeChannels),
    };

    /// <summary>Starts recording <paramref name="source"/>. Throws (COM or invalid operation) when Windows refuses every format.</summary>
    public static async Task<PipeCapture> StartAsync(PipeSource source)
    {
        Exception? last = null;
        foreach (var format in Formats)
        {
            WasapiRecorder? recorder = null;
            try
            {
                recorder = await BuildAsync(source, format).ConfigureAwait(false);
                var capture = new PipeCapture(recorder);
                recorder.StartRecording();
                return capture;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                // This format was refused: free the half-made recorder and try the next one.
                recorder?.Dispose();
                last = ex;
            }
        }
        throw new InvalidOperationException($"Windows refused to record the sound: {last?.Message}", last);
    }

    private static Task<WasapiRecorder> BuildAsync(PipeSource source, WaveFormat format)
    {
        var builder = new WasapiRecorderBuilder()
            .WithFormat(format)
            .WithBufferLength(CoreTiming.PipeCaptureBufferMs)
            .WithMmcssThreadPriority("Audio");
        if (source.DeviceId is { } deviceId)
        {
            using var devices = new MMDeviceEnumerator();
            var device = builder.WithDevice(devices.GetDevice(deviceId));
            return Task.FromResult((source.Loopback ? device.WithLoopbackCapture() : device).Build());
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, AudioEffectsProbe.ProcessLoopbackBuild))
            throw new InvalidOperationException("Recording one program needs Windows 10 version 2004 or later.");
        return builder.WithProcessLoopback((uint)source.ProcessId).BuildAsync();
    }

    /// <summary>Capture thread: converts the packet to float, notes its peak and queues it. No allocation, no lock.</summary>
    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        ReadOnlySpan<float> samples;
        if (_pcm16)
        {
            var shorts = MemoryMarshal.Cast<byte, short>(buffer);
            var n = Math.Min(shorts.Length, _scratch.Length);
            for (var i = 0; i < n; i++) _scratch[i] = shorts[i] / 32768f;
            samples = _scratch.AsSpan(0, n);
        }
        else
        {
            samples = MemoryMarshal.Cast<byte, float>(buffer);
        }

        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        NotePeak(peak);
        Ring.Write(samples);
    }

    private void NotePeak(float peak)
    {
        var bits = BitConverter.SingleToInt32Bits(peak);
        int seen;
        do
        {
            seen = Volatile.Read(ref _peakBits);
            if (BitConverter.Int32BitsToSingle(seen) >= peak) return;
        }
        while (Interlocked.CompareExchange(ref _peakBits, bits, seen) != seen);
    }

    /// <summary>The loudest raw sample since the last call (0 = digital silence or nothing arrived).</summary>
    public float TakePeak() => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _peakBits, 0));

    public void Dispose()
    {
        _recorder.DataAvailable -= OnData;
        _recorder.Dispose();
    }
}
