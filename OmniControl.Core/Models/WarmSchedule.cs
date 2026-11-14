namespace OmniControl.Core.Models;

/// <summary>
/// The arithmetic of the warm-light schedule (pure): from which minute of the day to which the picture warms towards a target
/// temperature, easing in over the first <see cref="RampMinutes"/> and out over the last, and what to say about it. Times are minutes
/// since midnight; a window may cross midnight (21:00 to 07:00).
/// </summary>
public static class WarmSchedule
{
    public const int DayMinutes = 24 * 60, StepMinutes = 30, RampMinutes = 30;

    /// <summary>How warm the schedule wants the picture at <paramref name="now"/> (minutes into the day, fractions allowed): 0 outside the window, 1 in its middle.</summary>
    public static double Factor(double now, int start, int end)
    {
        var length = Mod(end - start);
        if (length == 0) return 0; // the same start and end is no window at all
        var at = Mod(now - start);
        if (at >= length) return 0;
        var ramp = Math.Min(RampMinutes, length / 2.0);
        return Math.Clamp(Math.Min(at / ramp, (length - at) / ramp), 0, 1);
    }

    /// <summary>
    /// The temperature to show: the user's own (<paramref name="userKelvin"/>) moved <paramref name="factor"/> of the way towards
    /// <paramref name="targetKelvin"/>, but never made cooler than the user chose, so a picture that is already warmer stays as it is.
    /// </summary>
    public static int Kelvin(int userKelvin, double factor, int targetKelvin) =>
        targetKelvin >= userKelvin ? userKelvin : (int)Math.Round(userKelvin + (targetKelvin - userKelvin) * Math.Clamp(factor, 0, 1));

    /// <summary>The honest state in a few words: "Off", "Active until 07:00" or "Starts at 21:00".</summary>
    public static string StateText(bool enabled, double now, int start, int end)
    {
        if (!enabled) return "Off";
        if (Mod(end - start) == 0) return "Choose a start and an end time";
        return Mod(now - start) < Mod(end - start) ? $"Active until {Clock(end)}" : $"Starts at {Clock(start)}";
    }

    /// <summary>"21:00" for a minute of the day.</summary>
    public static string Clock(int minute) => $"{Mod(minute) / 60:00}:{Mod(minute) % 60:00}";

    /// <summary>The times the schedule's drop-downs offer, every <see cref="StepMinutes"/>.</summary>
    public static IEnumerable<int> Times() => Enumerable.Range(0, DayMinutes / StepMinutes).Select(i => i * StepMinutes);

    private static double Mod(double minutes) => (minutes % DayMinutes + DayMinutes) % DayMinutes;

    private static int Mod(int minutes) => (minutes % DayMinutes + DayMinutes) % DayMinutes;
}
