using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// The master list of conditions, for "and" in a Do rule and "While" in a Block rule: on battery, plugged in, between two times,
/// on some days, a program running. Stored as "battery", "plugged", "between|fh=22;th=7", "days|d5=0;d6=0", "running:chrome".
/// One registration per condition: the picker, the sentence and the check all read it.
public static class AutomationConditions
{
    private const string Power = "Power", When = "Time", Programs = "Programs";
    public const string Battery = "battery", Plugged = "plugged", Between = "between", Days = "days", Running = "running";

    public static readonly ActionOption FromHour = ActionOption.Number("fh", "From hour", 22, 0, 23);
    public static readonly ActionOption FromMinute = ActionOption.Number("fm", "From minute", 0, 0, 59);
    public static readonly ActionOption ToHour = ActionOption.Number("th", "Until hour", 7, 0, 23);
    public static readonly ActionOption ToMinute = ActionOption.Number("tm", "Until minute", 0, 0, 59);

    /// The picker's list; a condition already in the rule (other than the one being changed) is greyed.
    public static IReadOnlyList<PickChoice> Choices(AutomationRule rule, IReadOnlyList<string> programs, string? editing)
    {
        var used = rule.Conditions.Where(c => c != editing).Select(c => MouseAction.Parse(c).Id).ToHashSet();
        string? Taken(string id) => used.Contains(id) ? "Already in this rule." : null;
        var list = new List<PickChoice>
        {
            new(Battery, Power, "On battery", Keywords: "unplugged laptop", Unavailable: Taken(Battery)),
            new(Plugged, Power, "Plugged in", Keywords: "charging mains ac laptop", Unavailable: Taken(Plugged)),
            new(Between, When, "Between two times", Keywords: "night hours clock schedule", Options: new[] { FromHour, FromMinute, ToHour, ToMinute },
                AskFirst: true, Unavailable: Taken(Between)),
            new(Days, When, "On certain days", Keywords: "weekdays weekend monday sunday schedule", Options: AutomationDays.Options, AskFirst: true,
                Unavailable: Taken(Days)),
        };
        list.AddRange(programs.Select(p => new PickChoice($"{Running}:{p}", Programs, $"{p} is running", Keywords: "app program open")));
        list.Add(new PickChoice($"{Running}:", Programs, "Another program is running", "notepad", "app program open type name",
            ParamLabel: "Program name, as Task Manager shows it"));
        return list;
    }

    /// How a stored condition reads in the sentence ("on battery", "between 22:00 and 07:00", "chrome is running").
    public static string PhraseOf(string stored)
    {
        var condition = MouseAction.Parse(stored);
        return condition.Id switch
        {
            Battery => "on battery",
            Plugged => "plugged in",
            Between => $"between {Time(condition.Options, FromHour, FromMinute)} and {Time(condition.Options, ToHour, ToMinute)}",
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
            case Between:
                var from = condition.Options.Int(FromHour) * 60 + condition.Options.Int(FromMinute);
                var until = condition.Options.Int(ToHour) * 60 + condition.Options.Int(ToMinute);
                var minute = now.Now.Hour * 60 + now.Now.Minute;
                return from <= until ? minute >= from && minute < until : minute >= from || minute < until; // past midnight when from > until
            default: return false;
        }
    }

    private static string Time(OptionValues values, ActionOption hour, ActionOption minute) => $"{values.Int(hour):00}:{values.Int(minute):00}";
}
