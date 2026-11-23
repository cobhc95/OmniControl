using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// Time-window helpers for "Between two times" triggers, plus migration and legacy evaluation for rules saved with the old conditions list.
/// There is no separate condition list anymore: "+ and" and Block rules' "While" pick from <see cref="AutomationTriggers"/>.
public static class AutomationConditions
{
    public const string Battery = "battery", Plugged = "plugged", Between = "between", Days = "days", Running = "running";

    public static readonly ActionOption FromHour = ActionOption.Number("fh", "From hour", 22, 0, 23);
    public static readonly ActionOption FromMinute = ActionOption.Number("fm", "From minute", 0, 0, 59);
    public static readonly ActionOption ToHour = ActionOption.Number("th", "Until hour", 7, 0, 23);
    public static readonly ActionOption ToMinute = ActionOption.Number("tm", "Until minute", 0, 0, 59);

    /// The settings of a time window, as the trigger "Between two times" takes them.
    public static readonly IReadOnlyList<ActionOption> WindowOptions = new[] { FromHour, FromMinute, ToHour, ToMinute };

    /// Converts stored conditions with a direct trigger/state equivalent into triggers on load (idempotent, saved once).
    /// "days" has no standalone trigger and stays in <see cref="AutomationRule.Conditions"/> for <see cref="AutomationJoins"/>.
    public static int Migrate(IEnumerable<AutomationRule> rules)
    {
        var moved = 0;
        foreach (var rule in rules)
        {
            var first = true;
            for (var i = 0; i < rule.Conditions.Count;)
            {
                if (ToTrigger(rule.Conditions[i]) is not { } trigger) { first = false; i++; continue; }
                var join = first ? AutomationJoins.And : (i < rule.ConditionJoins.Count ? rule.ConditionJoins[i] : AutomationJoins.And);
                rule.AddTrigger(trigger, join);
                rule.RemoveCondition(i);
                first = false;
                moved++;
            }
        }
        return moved;
    }

    private static string? ToTrigger(string stored)
    {
        var condition = MouseAction.Parse(stored);
        return condition.Id switch
        {
            Battery => AutomationTriggers.StateTrigger(PowerStates.OnBattery, StateTriggerMath.TurnsOn),
            Plugged => AutomationTriggers.StateTrigger(PowerStates.OnBattery, StateTriggerMath.TurnsOff),
            Between => stored,
            Running when ProgramStates.IdOf(condition.Param) is var id && id != ProgramStates.Prefix =>
                AutomationTriggers.StateTrigger(id, StateTriggerMath.TurnsOn),
            _ => null,
        };
    }

    /// How a stored condition reads in the sentence ("on battery", "between 22:00 and 07:00", "chrome is running").
    public static string PhraseOf(string stored)
    {
        var condition = MouseAction.Parse(stored);
        return condition.Id switch
        {
            Battery => "on battery",
            Plugged => "plugged in",
            Between => WindowOf(condition.Options),
            Days => AutomationDays.PhraseOf(condition.Options),
            Running => $"{ProgramNames.Normalize(condition.Param)} is running",
            _ => "(unknown condition)",
        };
    }

    /// True when the stored condition holds now. An unknown condition never holds, so it can never allow or block by mistake.
    public static bool Holds(string stored, AutomationContext now)
    {
        var condition = MouseAction.Parse(stored);
        switch (condition.Id)
        {
            case Battery: return now.OnBattery == true;
            case Plugged: return now.OnBattery == false;
            case Days: return AutomationDays.Includes(condition.Options, now.Now.DayOfWeek);
            case Running: return now.Programs.Contains(ProgramNames.Normalize(condition.Param));
            case Between: return InWindow(condition.Options, now.Now);
            default: return false;
        }
    }

    /// "between 22:00 and 07:00": the condition and the trigger of the same name read alike.
    public static string WindowOf(OptionValues values) => $"between {Time(values, FromHour, FromMinute)} and {Time(values, ToHour, ToMinute)}";

    /// The first minute of a window and the minute it ends (minutes of the day).
    public static (int From, int Until) MinutesOf(OptionValues values) =>
        (values.Int(FromHour) * 60 + values.Int(FromMinute), values.Int(ToHour) * 60 + values.Int(ToMinute));

    public static bool InWindow(OptionValues values, DateTime now)
    {
        var (from, until) = MinutesOf(values);
        var minute = now.Hour * 60 + now.Minute;
        return from <= until ? minute >= from && minute < until : minute >= from || minute < until; // past midnight when from > until
    }

    private static string Time(OptionValues values, ActionOption hour, ActionOption minute) => $"{values.Int(hour):00}:{values.Int(minute):00}";
}
