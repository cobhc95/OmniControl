namespace OmniControl.Core.Engine;

/// A lock-free ring of samples between exactly one writer thread (a capture) and one reader thread (the playback). Allocates only
/// when it is made; reads and writes never block and never allocate, so they are safe inside audio callbacks.
internal sealed class AudioRing
{
    private readonly float[] _buffer;
    private long _written; // total samples ever written (only the writer changes it)
    private long _read;    // total samples ever read (only the reader changes it)

    public AudioRing(int capacity) => _buffer = new float[capacity];

    /// Samples waiting to be read.
    public int Count => (int)(Volatile.Read(ref _written) - Volatile.Read(ref _read));

    /// Writer side. When the reader has fallen too far behind, the newest samples that do not fit are dropped.
    public void Write(ReadOnlySpan<float> samples)
    {
        var free = _buffer.Length - Count;
        if (samples.Length > free) samples = samples[..free];
        var at = (int)(_written % _buffer.Length);
        var first = Math.Min(samples.Length, _buffer.Length - at);
        samples[..first].CopyTo(_buffer.AsSpan(at));
        samples[first..].CopyTo(_buffer);
        Volatile.Write(ref _written, _written + samples.Length);
    }

    /// Reader side: adds up to <paramref name="into"/>.Length samples onto it (mixing); returns how many there were.
    public int MixInto(Span<float> into)
    {
        var n = Math.Min(into.Length, Count);
        var at = (int)(_read % _buffer.Length);
        for (var i = 0; i < n; i++)
        {
            into[i] += _buffer[at];
            if (++at == _buffer.Length) at = 0;
        }
        Volatile.Write(ref _read, _read + n);
        return n;
    }

    /// Reader side: throws away the oldest samples so that only <paramref name="keep"/> remain.
    public void DropTo(int keep)
    {
        var drop = Count - keep;
        if (drop > 0) Volatile.Write(ref _read, _read + drop);
    }
}
