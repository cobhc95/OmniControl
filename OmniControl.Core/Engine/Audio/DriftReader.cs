using NAudio.Dsp;

namespace OmniControl.Core.Engine;

/// Reads a ring that another clock fills (a capture, or the first device of a pipe for an extra device) on the reader's own clock
/// without clicks: the ring is read through a resampler whose rate is nudged from how full the ring is (at most
/// <see cref="CoreTiming.PipeDriftMaxShift"/>), so it stays near its target. A ring that still grows past
/// <see cref="CoreTiming.PipeMaxQueueMs"/> is trimmed back to the target; one that runs dry gives silence until it has gathered
/// <see cref="CoreTiming.PipePrefillMs"/> again. Runs on the reader's audio thread: no locks; the resampler's buffers grow during the
/// first blocks and are reused from then on. Used by <see cref="ExtraOutput"/> and by every capture of an <see cref="AudioPipe"/>.
internal sealed class DriftReader
{
    private const int Channels = CoreTiming.PipeChannels;
    private const int SamplesPerMs = CoreTiming.PipeSamplesPerMs;

    private readonly WdlResampler _resampler = new();
    private readonly int _targetMs;
    private double _fillMs;
    private bool _primed;

    /// <param name="ring">The ring to read (the other thread writes it).</param>
    /// <param name="targetMs">How much it aims to keep waiting: more is safer against bursts, less is less delay.</param>
    public DriftReader(AudioRing ring, int targetMs)
    {
        Ring = ring;
        _targetMs = targetMs;
        _fillMs = targetMs;
        _resampler.SetMode(true, 2, false, 64, 32); // linear interpolation with a light low-pass, NAudio's own resampler default
        _resampler.SetFilterParms(0.693f, 0.707f);
        _resampler.SetFeedMode(false); // asked for output frames; it says how many input frames it needs
        _resampler.SetRates(CoreTiming.PipeSampleRate, CoreTiming.PipeSampleRate);
    }

    public AudioRing Ring { get; }

    public int QueuedMs => Ring.Count / SamplesPerMs;

    /// Fills <paramref name="block"/> (whole frames, interleaved) with the next sound, overwriting it; silence while not primed.
    public void Read(Span<float> block)
    {
        var frames = block.Length / Channels;
        if (Ring.Count > CoreTiming.PipeMaxQueueMs * SamplesPerMs)
        {
            Ring.DropTo(_targetMs * SamplesPerMs); // far behind (the writer burst or the reader stalled): start again from the target
            _fillMs = _targetMs;
        }
        if (!_primed && Ring.Count < CoreTiming.PipePrefillMs * SamplesPerMs)
        {
            block.Clear();
            return;
        }
        _primed = true;
        _fillMs += ((double)Ring.Count / SamplesPerMs - _fillMs) * CoreTiming.PipeDriftSmoothing;
        var shift = Math.Clamp((_fillMs - _targetMs) * CoreTiming.PipeDriftShiftPerMs, -CoreTiming.PipeDriftMaxShift, CoreTiming.PipeDriftMaxShift);
        _resampler.SetRates(CoreTiming.PipeSampleRate * (1 + shift), CoreTiming.PipeSampleRate); // a fuller ring is read a little faster

        var needed = _resampler.ResamplePrepare(frames, Channels, out var input);
        var wanted = input[..(needed * Channels)];
        wanted.Clear();
        if (Ring.MixInto(wanted) < wanted.Length) _primed = false; // ran dry: the gap is silence, then gather the prefill again
        var made = _resampler.ResampleOut(block, needed, frames, Channels);
        if (made < frames) block[(made * Channels)..].Clear();
    }
}
