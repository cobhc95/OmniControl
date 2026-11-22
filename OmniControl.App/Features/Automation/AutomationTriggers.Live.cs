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
    DateTime? ChangedAt(string stateId);
    IEnumerable<HotkeyAction> Actions { get; }
    HotkeyAction? Action(string id);
}

// The two live trigger families: the states (one entry per registered state, in a section per page) and "When an action runs" (one
// entry per master action). Nothing here is listed by hand: both are read from their master lists each time.
public static partial class AutomationTriggers
{
    public const string StateChanges = "state", ActionRuns = "action.runs";
    private const string ActionsSection = "When an action runs";
    private const string TriggersPart = "Triggers", StatesPart = "States";
    private const string WhenKey = "when", LevelKey = "v", ChoiceKey = "is";
    private const string MissingState = "a state that is no longer available", MissingAction = "an action that is no longer available";

    /// The title and search words of the one window that picks a trigger or a state (a rule's trigger, a preset's "State…").
    public const string PickTitle = "Choose a trigger or state", SearchText = "Search triggers, states and keys…";

    /// The picker's whole list, under "Triggers" the fixed ones and one per action, under "States" one section per page (its Title) with
    /// one entry per state. <paramref name="keep"/> is the stored trigger being edited: a state of a side list it names is listed on its own, so it shows picked.
    public static IReadOnlyList<PickChoice> Choices(IReadOnlyList<string> programs, ITriggerSources sources, string keep = "") =>
        FixedChoices(programs).Concat(sources.Actions.Select(ActionChoice)).Select(c => c with { Part = TriggersPart })
            .Concat(StateChoices(sources, keep))
            .ToList();

    /// The settings of a state's trigger, by its kind. ONE place: the picker shows them and the check reads them.
    private static IReadOnlyList<ActionOption> OptionsOf(StateEntry state, IReadOnlyList<string> choices) => state.Kind switch
    {
        StateKind.OnOff => new[] { ActionOption.Choice(WhenKey, WhenLabel, StateTriggerMath.WhensOf(StateKind.OnOff)) },
        StateKind.Level => new[]
        {
            ActionOption.Choice(WhenKey, WhenLabel, StateTriggerMath.WhensOf(StateKind.Level)),
            ActionOption.Number(LevelKey, "Value", (state.Min + state.Max) / 2, state.Min, state.Max, 1, state.Unit.Length > 0 ? " " + state.Unit : string.Empty),
        },
        // A choice list can change order (devices come and go), so the picked value is always stored: the default is a placeholder.
        _ => choices.Count == 0 ? Array.Empty<ActionOption>() : new[] { ActionOption.Choice(ChoiceKey, "Becomes", choices.Prepend(ChooseValue).ToList()) },
    };

    private const string ChooseValue = "(choose a value)", WhenLabel = "Run when it";

    private static IReadOnlyList<string> ChoicesOf(StateEntry state) => state.Choices?.Invoke() ?? Array.Empty<string>();

    /// A state as the picker lists it: in its page's section (its Title) under its own heading, its device muted after the name, its value
    /// now as a tag, and "Now Off · changed 12 min ago" under the settings title (how it is known in its tooltip).
    private static PickChoice StateChoice(StateEntry state, ITriggerSources sources)
    {
        var choices = ChoicesOf(state);
        var now = sources.Now(state.Id);
        var value = Capital(state.Words(now));
        var changed = sources.ChangedAt(state.Id) is { } at ? $" · changed {Ago(DateTime.Now - at)}" : string.Empty;
        var read = now?.At is { } readAt ? $"Read {readAt:HH:mm}. " : string.Empty;
        return new PickChoice($"{StateChanges}:{state.Id}", state.Group, state.Name, Keywords: $"state value {value} {state.Note} {state.Keywords}",
            Options: OptionsOf(state, choices), AskFirst: true, Heading: state.Heading, Part: StatesPart, Detail: state.Note, Value: value,
            Status: $"Now {value}{changed}", StatusTip: read + state.HowKnown,
            Unavailable: state.Kind == StateKind.Choice && choices.Count == 0 ? "There is nothing to choose from yet." : null, Remote: state.RemotePc);
    }

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpper(words[0]) + words[1..];

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "just now" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago" : $"{(int)span.TotalHours} h ago";

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

    /// True while a state trigger's condition holds now (its state is on, above its value...) or the clock is in a window; never for any other trigger, which is a moment.
    public static bool HoldsNow(string stored, ITriggerSources sources)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id == Between) return AutomationConditions.InWindow(trigger.Options, DateTime.Now);
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

    /// "When this on/off state turns on", as stored.
    public static string WhileOn(string stateId) => $"{StateChanges}:{stateId}|{WhenKey}={Uri.EscapeDataString(StateTriggerMath.TurnsOn)}";

    /// The state a "turns on" state trigger follows, or null for any other trigger.
    public static string? WhileOnState(string stored)
    {
        var trigger = MouseAction.Parse(stored);
        return trigger.Id == StateChanges && trigger.Options.Text(ActionOption.Choice(WhenKey, string.Empty, StateTriggerMath.WhensOf(StateKind.OnOff))) == StateTriggerMath.TurnsOn
            ? trigger.Param : null;
    }

    private static IEnumerable<PickChoice> StateChoices(ITriggerSources sources, string keep) =>
        Listed(sources, s => StateChoice(s, sources), MouseAction.Parse(keep) is { Id: StateChanges } kept ? kept.Param : null);

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
