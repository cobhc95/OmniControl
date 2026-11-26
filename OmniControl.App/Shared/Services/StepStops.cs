namespace OmniControl.App.Shared.Services;

/// The "snap to round steps" rule of a wheel step: the stops are the multiples of the step counted from 0, and the range's own ends are
/// stops too, so the minimum and maximum stay reachable whatever the step is.
internal static class StepStops
{
    /// The stop a step of <paramref name="step"/> goes to from <paramref name="level"/>: the next one up, or the one below. A level already at
    /// the end stays there; a step of 0 or less goes nowhere.
    public static int Next(int level, int step, bool up, int min, int max)
    {
        level = Math.Clamp(level, min, max);
        if (step <= 0) return level;
        var index = level / (double)step;
        var stop = up ? ((int)Math.Floor(index) + 1) * step : ((int)Math.Ceiling(index) - 1) * step;
        return Math.Clamp(stop, min, max);
    }
}
