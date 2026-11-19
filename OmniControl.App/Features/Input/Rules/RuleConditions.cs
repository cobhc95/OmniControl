using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Rules;

/// The master list of rule conditions: over a screen area (every <see cref="PointerAreaKind"/>, named as the gesture cards name it),
/// in a program (the program under the pointer, or the active one for a shortcut trigger), keys held (every combination of Ctrl, Alt,
/// Shift and Win; held keys count exactly, as for gestures). Stored as "area.taskbar", "program:chrome", "held:Ctrl+Shift".
/// The mouse layer is the rule's own Layer setting, not a condition.
public static class RuleConditions
{
    private const string AreaCategory = "Over a screen area", ProgramCategory = "In a program", HeldCategory = "Keys held";
    private const string AreaPrefix = "area.", Program = "program", Held = "held";
    private const string AlreadyThere = "Already in this rule.";
    private const string ShortcutHolds = "The shortcut that triggers this rule already says which keys are held.";

    private static readonly PointerAreaKind[] Areas = Enum.GetValues<PointerAreaKind>().Where(a => a != PointerAreaKind.Elsewhere).ToArray();
    private static readonly (Modifiers Key, string Name)[] KeyNames = { (Modifiers.Ctrl, "Ctrl"), (Modifiers.Alt, "Alt"), (Modifiers.Shift, "Shift"), (Modifiers.Win, "Win") };

    // Every combination of the four modifier keys, fewest keys first.
    private static readonly Modifiers[] KeySets = Enumerable.Range(1, 15).Select(n => (Modifiers)n)
        .OrderBy(m => KeyNames.Count(k => m.HasFlag(k.Key))).ThenBy(m => (int)m).ToArray();

    private static string AreaSpec(PointerAreaKind area) => AreaPrefix + area.ToString().ToLowerInvariant();

    private static string KeysText(Modifiers keys, string separator) =>
        string.Join(separator, KeyNames.Where(k => keys.HasFlag(k.Key)).Select(k => k.Name));

    /// The picker's list for one condition slot of <paramref name="rule"/>. Programs come from <paramref name="programs"/> (normalised
    /// names). An entry already used by another condition, or a "keys held" entry under a shortcut trigger, is greyed with the reason.
    /// <param name="editing">The stored text of the condition being changed (it is not "already there"), or null for a new one.</param>
    public static IReadOnlyList<PickChoice> Choices(Rule rule, IReadOnlyList<string> programs, string? editing)
    {
        var used = rule.Conditions.Where(c => c != editing).ToHashSet();
        var shortcut = RuleTriggers.Of(rule.Trigger)?.Kind == TriggerKind.Shortcut;
        string? Taken(string spec) => used.Contains(spec) ? AlreadyThere : null;

        var list = new List<PickChoice>();
        list.AddRange(Areas.Select(a => new PickChoice(AreaSpec(a), AreaCategory, GestureCatalog.AreaName(a),
            Keywords: "screen area gesture", Unavailable: Taken(AreaSpec(a)))));
        list.AddRange(programs.Select(p => new PickChoice($"{Program}:{p}", ProgramCategory, p, Keywords: "app program window",
            Unavailable: Taken($"{Program}:{p}"))));
        list.Add(new PickChoice($"{Program}:", ProgramCategory, "Another program", "notepad", "app program window type name",
            ParamLabel: "Program name, as Task Manager shows it"));
        foreach (var keys in KeySets)
        {
            var spec = $"{Held}:{KeysText(keys, "+")}";
            list.Add(new PickChoice(spec, HeldCategory, $"{KeysText(keys, " + ")} held", Keywords: "modifier keys keyboard",
                Unavailable: shortcut ? ShortcutHolds : Taken(spec)));
        }
        return list;
    }

    /// How a stored condition reads in the rule's sentence ("over the taskbar", "in chrome", "Ctrl + Shift held").
    public static string PhraseOf(string stored)
    {
        if (AreaOf(stored) is { } area) return $"over the {GestureCatalog.AreaName(area).ToLowerInvariant()}";
        if (ProgramOf(stored) is { } program) return $"in {program}";
        if (KeysOf(stored) is { } keys) return $"{KeysText(keys, " + ")} held";
        return "(unknown condition)";
    }

    /// The screen area a stored condition names, or null when it is not an area condition.
    public static PointerAreaKind? AreaOf(string stored) => Areas.Cast<PointerAreaKind?>().FirstOrDefault(a => AreaSpec(a!.Value) == stored);

    /// The program (normalised name) a stored condition names, or null when it is not a program condition (or names none).
    public static string? ProgramOf(string stored)
    {
        var parsed = MouseAction.Parse(stored);
        return parsed.Id == Program && ProgramNames.Normalize(parsed.Param) is { Length: > 0 } name ? name : null;
    }

    /// The keys a stored "keys held" condition names, or null when it is not one.
    public static Modifiers? KeysOf(string stored)
    {
        var parsed = MouseAction.Parse(stored);
        if (parsed.Id != Held) return null;
        var keys = Modifiers.None;
        foreach (var part in parsed.Param.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            keys |= KeyNames.FirstOrDefault(k => k.Name == part).Key;
        return keys == Modifiers.None ? null : keys;
    }
}
