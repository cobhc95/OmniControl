namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// The equaliser: one biquad per band and channel (transposed direct form II, double precision), then the overall gain. Every band
/// keeps its place even when flat (a flat band is skipped once it has settled, which costs nothing because a flat filter holds no
/// state), and state is matched to bands by their id, so it always stays with its own band. On an edit each band's coefficients
/// move in a straight line to the new ones; a straight line between two stable biquads stays stable (the stable region of a1, a2 is
/// a triangle). A new band fades in from flat; a removed band is kept in a spare slot while it fades out to flat, so adding or
/// removing a band does not click.
internal sealed class EqStage : IDspStage
{
    /// Filter state smaller than this (about -400 dB) is set to zero, so silence never decays into slow denormal numbers.
    private const double StateFloor = 1e-20;

    /// The id of an empty spare slot (band ids start at 1).
    private const int FreeSlot = 0;

    // Slots [0, _bands) are the bands in order; the rest are spare slots for removed bands while they fade out.
    private readonly int[] _ids;
    private readonly BiquadCoefficients[] _target;
    private readonly BiquadCoefficients[] _now;
    private readonly BiquadCoefficients[] _step;
    private readonly int[] _glideLeft; // frames left in each slot's glide
    private readonly double[] _state; // per slot and channel: z1, z2
    private readonly int _bands;
    private readonly int _channels;
    private Glide _gain;

    /// The sound entering the equaliser, for the graph's live spectrum (off until the graph asks).
    public SpectrumTap Tap { get; } = new();

    public EqStage(IReadOnlyList<EqBand> bands, double gainDb, int sampleRate, int channels)
    {
        _bands = Math.Min(bands.Count, Eq.MaxBands);
        var slots = _bands + Eq.MaxBands;
        _ids = new int[slots];
        _target = new BiquadCoefficients[slots];
        for (var i = 0; i < slots; i++)
        {
            _ids[i] = i < _bands ? bands[i].Id : FreeSlot;
            _target[i] = i < _bands ? EqMath.Coefficients(bands[i], sampleRate) : BiquadCoefficients.Identity;
        }
        _now = (BiquadCoefficients[])_target.Clone();
        _step = new BiquadCoefficients[slots];
        _glideLeft = new int[slots];
        _state = new double[slots * channels * 2];
        _channels = channels;
        _gain = new Glide((float)Math.Pow(10, gainDb / 20));
    }

    public void Process(Span<float> samples, int channels)
    {
        Tap.Write(samples, channels);
        var frames = samples.Length / channels;
        for (var b = 0; b < _now.Length; b++)
        {
            if (IsSettledFlat(b)) continue;
            var glide = Math.Min(_glideLeft[b], frames);
            for (var ch = 0; ch < channels; ch++) Filter(samples, channels, b, ch, glide);
            Advance(b, glide);
        }
        ApplyGain(samples, channels);
    }

    private void ApplyGain(Span<float> samples, int channels) => _gain.Apply(samples, channels);

    private void Filter(Span<float> samples, int channels, int slot, int ch, int glide)
    {
        var at = (slot * channels + ch) * 2;
        double z1 = _state[at], z2 = _state[at + 1];
        var c = _now[slot];
        var s = _step[slot];
        double b0 = c.B0, b1 = c.B1, b2 = c.B2, a1 = c.A1, a2 = c.A2;
        var frame = 0;
        for (var i = ch; i < samples.Length; i += channels, frame++)
        {
            if (frame < glide)
            {
                b0 += s.B0; b1 += s.B1; b2 += s.B2; a1 += s.A1; a2 += s.A2;
            }
            double x = samples[i];
            var y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            samples[i] = (float)y;
        }
        _state[at] = Flush(z1);
        _state[at + 1] = Flush(z2);
    }

    /// Moves the slot's coefficients on by the frames just glided; the last step lands exactly on the target.
    private void Advance(int slot, int glided)
    {
        if (glided == 0) return;
        _glideLeft[slot] -= glided;
        if (_glideLeft[slot] == 0)
        {
            _now[slot] = _target[slot];
            return;
        }
        var c = _now[slot];
        var s = _step[slot];
        _now[slot] = new BiquadCoefficients(c.B0 + s.B0 * glided, c.B1 + s.B1 * glided, c.B2 + s.B2 * glided,
            c.A1 + s.A1 * glided, c.A2 + s.A2 * glided);
    }

    /// A flat slot whose state has run out (two samples at Identity clear it) passes sound unchanged: skipping it is exact.
    private bool IsSettledFlat(int slot) => _glideLeft[slot] == 0 && _now[slot] == BiquadCoefficients.Identity && StateIsZero(slot);

    private bool StateIsZero(int slot)
    {
        var at = slot * _channels * 2;
        for (var i = at; i < at + _channels * 2; i++)
            if (_state[i] != 0) return false;
        return true;
    }

    /// Zero for values too small to hear, and for a broken value (NaN or infinity), so one bad sample cannot silence the band for good.
    private static double Flush(double z) => double.IsFinite(z) && Math.Abs(z) >= StateFloor ? z : 0;

    /// Audio thread, no allocation: bands carry on from the band with the same id; new bands fade in, removed ones fade out.
    public void TakeStateFrom(IDspStage previous)
    {
        if (previous is not EqStage eq || eq._channels != _channels) return;
        for (var b = 0; b < _bands; b++)
        {
            var from = eq.SlotOf(_ids[b]);
            if (from >= 0) CopySlot(eq, from, b);
            else _now[b] = BiquadCoefficients.Identity; // a new band: starts flat and glides in
            StartGlide(b);
        }
        var spare = _bands;
        for (var j = 0; j < eq._ids.Length && spare < _ids.Length; j++)
        {
            if (eq._ids[j] == FreeSlot || SlotOf(eq._ids[j]) >= 0 || eq.IsSettledFlat(j)) continue;
            _ids[spare] = eq._ids[j];
            CopySlot(eq, j, spare);
            StartGlide(spare); // its target is Identity: it fades out
            spare++;
        }
        _gain.StartFrom(eq._gain.Now);
    }

    private int SlotOf(int id)
    {
        if (id == FreeSlot) return -1;
        for (var i = 0; i < _ids.Length; i++)
            if (_ids[i] == id) return i;
        return -1;
    }

    private void CopySlot(EqStage from, int fromSlot, int toSlot)
    {
        _now[toSlot] = from._now[fromSlot];
        var size = _channels * 2;
        Array.Copy(from._state, fromSlot * size, _state, toSlot * size, size);
    }

    private void StartGlide(int slot)
    {
        var from = _now[slot];
        var to = _target[slot];
        if (from == to) return;
        double n = PipeFormat.GlideFrames;
        _step[slot] = new BiquadCoefficients((to.B0 - from.B0) / n, (to.B1 - from.B1) / n, (to.B2 - from.B2) / n,
            (to.A1 - from.A1) / n, (to.A2 - from.A2) / n);
        _glideLeft[slot] = PipeFormat.GlideFrames;
    }
}
