using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

/// What the live trigger families are read from, each time: the master list of states and the master list of actions.
public interface ITriggerSources
{
    IReadOnlyList<StateEntry> States { get; }
    StateEntry? State(string id);
    StateValue? Now(string stateId);
    IEnumerable<HotkeyAction> Actions { get; }
    HotkeyAction? Action(string id);
}

// The two live trigger families: "When a state changes" (one entry per registered state) and
// "When an action runs" (one entry per master action). Nothing here is listed by hand: both are read from their master lists each time.
public static partial class AutomationTriggers
{
    public const string StateChanges = "state", ActionRuns = "action.runs";
    private const string StatesSection = "When a state changes", ActionsSection = "When an action runs";
    private const string WhenKey = "when", LevelKey = "v", ChoiceKey = "is", NowKey = "now";
    private const string MissingState = "a state that is no longer available", MissingAction = "an action that is no longer available";

    /// The picker's whole list: the fixed triggers, then one per state, then one per action.
    public static IReadOnlyList<PickChoice> Choices(IReadOnlyList<string> programs, ITriggerSources sources) =>
        FixedChoices(programs)
            .Concat(sources.States.Select(s => StateChoice(s, sources.Now(s.Id))))
            .Concat(sources.Actions.Select(ActionChoice))
            .ToList();

    /// The settings of a state's trigger, by its kind. ONE place: the picker shows them and the check reads them.
    private static IReadOnlyList<ActionOption> OptionsOf(StateEntry state, IReadOnlyList<string> choices) => state.Kind switch
    {
        StateKind.OnOff => new[] { ActionOption.Choice(WhenKey, "When it", StateTriggerMath.WhensOf(StateKind.OnOff)) },
        StateKind.Level => new[]
        {
            ActionOption.Choice(WhenKey, "When it", StateTriggerMath.WhensOf(StateKind.Level)),
            ActionOption.Number(LevelKey, "Value", (state.Min + state.Max) / 2, state.Min, state.Max, 1, state.Unit.Length > 0 ? " " + state.Unit : string.Empty),
        },
        // A choice list can change order (devices come and go), so the picked value is always stored: the default is a placeholder.
        _ => choices.Count == 0 ? Array.Empty<ActionOption>() : new[] { ActionOption.Choice(ChoiceKey, "Becomes", choices.Prepend(ChooseValue).ToList()) },
    };

    private const string ChooseValue = "(choose a value)";

    private static IReadOnlyList<string> ChoicesOf(StateEntry state) => state.Choices?.Invoke() ?? Array.Empty<string>();

    private static PickChoice StateChoice(StateEntry state, StateValue? now)
    {
        var choices = ChoicesOf(state);
        var known = now?.At is { } at ? $"read {at:HH:mm} · " : string.Empty;
        var options = new[] { ActionOption.Heading(NowKey, $"Now: {state.Words(now)}", known + state.HowKnown) }.Concat(OptionsOf(state, choices)).ToList();
        return new PickChoice($"{StateChanges}:{state.Id}", StatesSection, state.Name, Keywords: $"state value {state.Group} {state.Keywords}",
            Options: options, AskFirst: true, Heading: state.Heading ?? state.Group, Tag: state.Note,
            Unavailable: state.Kind == StateKind.Choice && choices.Count == 0 ? "There is nothing to choose from yet." : null, Remote: state.RemotePc);
    }

    private static PickChoice ActionChoice(HotkeyAction action) =>
        new($"{ActionRuns}:{action.Id}", ActionsSection, action.Title, Keywords: $"action runs ran {action.Keywords}", Heading: action.PickerHeading, Remote: action.RemotePc);

    /// How any stored trigger reads in the sentence ("when L530 bulb turns on", "when Volume up runs").
    public static string PhraseOf(string stored, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        switch (trigger.Id)
        {
            case StateChanges:
                if (sources.State(trigger.Param) is not { } state) return $"when {MissingState} changes";
                var options = trigger.Options;
                var settings = OptionsOf(state, ChoicesOf(state));
                return state.Kind switch
                {
                    StateKind.OnOff => $"when {state.Name} {options.Text(settings[0])}",
                    StateKind.Level => $"when {state.Name} {options.Text(settings[0])} {options.Int(settings[1])}{(state.Unit.Length > 0 ? " " + state.Unit : string.Empty)}",
                    _ => $"when {state.Name} becomes {(settings.Count > 0 ? options.Text(settings[0]) : "(nothing)")}",
                };
            case ActionRuns:
                return $"when {sources.Action(trigger.Param)?.Title ?? MissingAction} runs";
            default:
                return FixedPhraseOf(trigger) ?? "(unknown trigger)";
        }
    }

    /// True when a stored trigger names a state or an action that is not registered now (it never fires; the rule shows it as missing).
    public static bool IsMissing(string stored, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        return trigger.Id switch
        {
            StateChanges => sources.State(trigger.Param) is null,
            ActionRuns => sources.Action(trigger.Param) is null,
            _ => false,
        };
    }

    /// True when the stored trigger is a state trigger on this state whose condition this change meets.
    public static bool FiresOn(string stored, StateChange change)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id != StateChanges || trigger.Param != change.Entry.Id || ConditionOf(change.Entry, trigger.Options) is not (var when, var level, var choice)) return false;
        return StateTriggerMath.Fires(change.Entry.Kind, change.Before, change.After, when, level, choice);
    }

    /// True while a state trigger's condition holds now (its state is on, above its value...); never for any other trigger, which is a moment.
    public static bool HoldsNow(string stored, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id != StateChanges || sources.State(trigger.Param) is not { } state || ConditionOf(state, trigger.Options) is not (var when, var level, var choice)) return false;
        return StateTriggerMath.Holds(state.Kind, sources.Now(state.Id), when, level, choice);
    }

    /// A state trigger's settings as <see cref="StateTriggerMath"/> takes them; null when the state offers none.
    private static (string When, double Level, string Choice)? ConditionOf(StateEntry state, OptionValues values)
    {
        var settings = OptionsOf(state, ChoicesOf(state));
        if (settings.Count == 0) return null;
        return state.Kind switch
        {
            StateKind.Level => (values.Text(settings[0]), values.Int(settings[1]), string.Empty),
            StateKind.Choice => (StateTriggerMath.Becomes, 0, values.Text(settings[0])),
            _ => (values.Text(settings[0]), 0, string.Empty),
        };
    }

    /// The states as the preset manager offers them: one section per page.
    public static IReadOnlyList<PickChoice> StateChoices(ITriggerSources sources) =>
        sources.States.Select(s => StateChoice(s, sources.Now(s.Id)) with { Category = s.Group, Heading = s.Heading }).ToList();

    /// The actions as every action picker shows them, each stored as "when it runs".
    public static IReadOnlyList<PickChoice> ActionChoices(IEnumerable<HotkeyAction> actions) =>
        actions.Select(a => MouseActionCatalog.OmniEntry(a) with { Spec = $"{ActionRuns}:{a.Id}" }).ToList();

    /// A trigger in a chip's few words: "Steam running" (a state turning on), "Volume above 50 %", "Action: Lock PC".
    public static string ChipOf(string stored, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id == ActionRuns) return $"Action: {sources.Action(trigger.Param)?.Title ?? MissingAction}";
        if (trigger.Id == StateChanges && sources.State(trigger.Param) is { Kind: StateKind.OnOff } state && ConditionOf(state, trigger.Options)?.When == StateTriggerMath.TurnsOn)
            return state.Name;
        var phrase = PhraseOf(stored, sources);
        return phrase.StartsWith("when ", StringComparison.Ordinal) ? phrase[5..] : phrase;
    }

    /// True when the stored trigger is "When this action runs" for <paramref name="action"/> (an old id read as its new one).
    public static bool FiresOn(string stored, HotkeyAction action, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        return trigger.Id == ActionRuns && sources.Action(trigger.Param) == action;
    }
}
