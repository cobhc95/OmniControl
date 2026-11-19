namespace OmniControl.App.Features.Volume.Audio.Engine;

/// The delay figure a row shows, kept steady: every reading is rounded to the nearest <see cref="Timing.DelayStepMs"/>, the last good
/// figure stays while a measurement is pending (a reading of 0), and a new figure replaces it only when the same rounded value was
/// read <see cref="Timing.DelayStableReadings"/> times in a row, so the number never flickers between values or in and out of view.
internal sealed class StableDelay
{
    private readonly int _step;
    private readonly int _readings;
    private int _candidate;
    private int _seen;

    public StableDelay(int step = Timing.DelayStepMs, int readings = Timing.DelayStableReadings)
    {
        _step = step;
        _readings = readings;
    }

    /// The figure to show, in whole ms; null until a first measurement arrives.
    public int? Shown { get; private set; }

    /// A delay rounded to the nearest step (halves up), never below one step for a real delay.
    public static int Round(int ms, int step = Timing.DelayStepMs) =>
        ms <= 0 ? 0 : Math.Max(step, (int)Math.Round(ms / (double)step, MidpointRounding.AwayFromZero) * step);

    /// Takes one reading (ms; 0 or less = not measured yet) and returns the figure to show.
    public int? Feed(int readingMs)
    {
        if (readingMs <= 0) return Shown; // a pending measurement never hides the last good figure
        var rounded = Round(readingMs, _step);
        if (Shown is null)
        {
            Shown = rounded;
            return Shown;
        }
        if (rounded == Shown)
        {
            _seen = 0;
            return Shown;
        }
        _seen = rounded == _candidate ? _seen + 1 : 1;
        _candidate = rounded;
        if (_seen >= _readings)
        {
            Shown = rounded;
            _seen = 0;
        }
        return Shown;
    }
}
