using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

/// Rules saved before programs were states followed "program.start:name" and "program.close:name" triggers. Each becomes the program's state
/// with the matching setting ("program.start:chrome" is "state:program.chrome" turning on, "program.close:chrome" turning off), which fires
/// at the same moments: the look that read the running programs now tells the state, and a program already running when a rule is switched
/// on still counts as a baseline, never as starting. Run at every load of the rules (start-up, an import), idempotent, saved once.
public static class ProgramTriggerMigration
{
    private const string LegacyStart = "program.start", LegacyClose = "program.close";

    /// Rewrites the program triggers of <paramref name="rules"/>; the number of triggers changed. A trigger with no program name (none can be
    /// made any more) stays as it was.
    public static int Run(IEnumerable<AutomationRule> rules)
    {
        var changed = 0;
        foreach (var rule in rules)
        {
            var first = Converted(rule.Trigger);
            if (first != rule.Trigger)
            {
                rule.Trigger = first;
                changed++;
            }
            for (var i = 0; i < rule.MoreTriggers.Count; i++)
            {
                var now = Converted(rule.MoreTriggers[i]);
                if (now == rule.MoreTriggers[i]) continue;
                rule.MoreTriggers[i] = now;
                changed++;
            }
        }
        return changed;
    }

    /// The stored trigger as it is now.
    public static string Converted(string stored)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id is not (LegacyStart or LegacyClose)) return stored;
        var id = ProgramStates.IdOf(trigger.Param);
        if (id == ProgramStates.Prefix) return stored;
        var when = trigger.Id == LegacyStart ? StateTriggerMath.TurnsOn : StateTriggerMath.TurnsOff;
        return AutomationTriggers.StateTrigger(id, when);
    }
}
