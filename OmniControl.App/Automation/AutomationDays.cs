using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;

namespace OmniControl.App.Automation;

/// <summary>The seven "on this day" settings shared by the time-of-day trigger and the days condition, and how a set of days reads in a sentence.</summary>
public static class AutomationDays
{
    private static readonly string[] Names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    /// <summary>One switch per day, Monday first, all on.</summary>
    public static readonly IReadOnlyList<ActionOption> Options =
        Names.Select((name, i) => ActionOption.Toggle($"d{i}", name, on: true)).ToList();

    /// <summary>True when the day is switched on in these values.</summary>
    public static bool Includes(OptionValues values, DayOfWeek day) => values.Bool(Options[Index(day)]);

    /// <summary>"every day", "on weekdays", "at weekends" or "on Mon, Wed".</summary>
    public static string PhraseOf(OptionValues values)
    {
        var on = Names.Where((_, i) => values.Bool(Options[i])).ToList();
        if (on.Count == Names.Length || on.Count == 0) return "every day";
        if (on.SequenceEqual(Names.Take(5))) return "on weekdays";
        if (on.SequenceEqual(Names.Skip(5))) return "at weekends";
        return "on " + string.Join(", ", on);
    }

    // Monday = 0 ... Sunday = 6 (DayOfWeek starts on Sunday).
    private static int Index(DayOfWeek day) => ((int)day + 6) % 7;
}
