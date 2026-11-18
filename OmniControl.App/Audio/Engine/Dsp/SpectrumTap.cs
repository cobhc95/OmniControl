namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>
/// The latest stretch of one point's sound, mixed to mono, kept for a live spectrum: one writer (an audio or capture thread) and one
/// reader (the analyser's worker). It records nothing until <see cref="Enabled"/> is set, so a chain whose graph is not on screen pays
/// nothing. A reader may meet a block half written; for a drawing that is harmless.
/// </summary>
internal sealed class SpectrumTap
{
    /// <summary>Frames kept; a power of two.</summary>
    public const int Length = 8192;

    private readonly float[] _buffer = new float[Length];
    private long _written;
    private volatile bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Frames ever written; unchanged while no sound arrives.</summary>
    public long Written => Volatile.Read(ref _written);

    /// <summary>Writer thread, allocation-free: keeps the block (interleaved, <paramref name="channels"/> per frame) when enabled.</summary>
    public void Write(ReadOnlySpan<float> samples, int channels)
    {
        if (!_enabled || channels < 1) return;
        var frames = samples.Length / channels;
        var at = _written;
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var ch = 0; ch < channels; ch++) sum += samples[f * channels + ch];
            _buffer[(at + f) & (Length - 1)] = sum / channels;
        }
        Volatile.Write(ref _written, at + frames);
    }

    /// <summary>Fills <paramref name="into"/> with the newest frames; false while fewer than that were written.</summary>
    public bool CopyLatest(Span<float> into)
    {
        var end = Written;
        if (end < into.Length) return false;
        var start = end - into.Length;
        for (var i = 0; i < into.Length; i++) into[i] = _buffer[(start + i) & (Length - 1)];
        return true;
    }
}
