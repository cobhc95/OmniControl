using System.Collections.Specialized;
using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Automation;

// The live triggers (docs/mockups/triggers-states-actions.html): "When a state changes" follows the master list of states, "When an action
// runs" follows the one run path of HotkeyRegistry. Both lists are read live, so anything registered anywhere is a trigger at once, and a
// rename, a removal or a merge reaches the rules here. Loops: a rule never starts from a chain it is already in, and a chain is cut at
// Timing.AutomationChainMax (the minimum gap and the burst pause still apply on top).
public sealed partial class AutomationService
{
    private readonly StateRegistry _states;
    private readonly ITriggerSources _sources;

    /// <summary>The lists the trigger picker reads changed (an action or a state came, went or was renamed): an open picker reads them again.</summary>
    public event Action? TriggersChanged;

    private sealed class Sources : ITriggerSources
    {
        private readonly HotkeyRegistry _hotkeys;
        private readonly StateRegistry _states;

        public Sources(HotkeyRegistry hotkeys, StateRegistry states) => (_hotkeys, _states) = (hotkeys, states);

        public IReadOnlyList<StateEntry> States => _states.States;
        public StateEntry? State(string id) => _states.Find(id);
        public StateValue? Now(string stateId) => _states.Now(stateId);
        public IEnumerable<HotkeyAction> Actions => _hotkeys.Actions;
        public HotkeyAction? Action(string id) => _hotkeys.Find(id);
    }

    private void StartLive()
    {
        _states.Register(PowerStates.Make().Append(PausedState()));
        _hotkeys.Ran += OnActionRan;
        _states.ValueChanged += OnStateChanged;
        _hotkeys.Actions.CollectionChanged += OnActionsListChanged;
        _states.ListChanged += OnListsChanged;
        _hotkeys.Moved += OnActionsMoved;
        _states.Moved += OnStatesMoved;
    }

    private void StopLive()
    {
        _hotkeys.Ran -= OnActionRan;
        _states.ValueChanged -= OnStateChanged;
        _hotkeys.Actions.CollectionChanged -= OnActionsListChanged;
        _states.ListChanged -= OnListsChanged;
        _hotkeys.Moved -= OnActionsMoved;
        _states.Moved -= OnStatesMoved;
        _states.Unregister(PowerStates.Ids.Append(PausedId));
    }

    private void NotifyPowerStates()
    {
        foreach (var id in PowerStates.Ids) _states.Notify(id);
    }

    public IReadOnlyList<PickChoice> TriggerChoices() => AutomationTriggers.Choices(ProgramNames.Known(_remembered()), _sources);

    /// <summary>How a stored trigger reads in the rule's sentence, with the names the states and actions have now.</summary>
    public string TriggerPhrase(string stored) => AutomationTriggers.PhraseOf(stored, _sources);

    /// <summary>What a rule names that is not registered now: its trigger, and an action it does or blocks (a removed device, a deleted script).</summary>
    public (bool Trigger, bool Action) MissingOf(AutomationRule rule) =>
        (rule.AllTriggers.Any(t => AutomationTriggers.IsMissing(t, _sources)),
         rule.Actions.Any(a => AutomationSteps.ActionIdOf(a) is { } id && _hotkeys.Find(id) is null));

    private void OnActionsListChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnListsChanged();

    private bool _listsChangePending;

    // A batch of registrations (a page's actions, a device renamed on every key) arrives as many changes: they are answered once, together.
    private void OnListsChanged()
    {
        if (_listsChangePending) return;
        _listsChangePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _listsChangePending = false;
            TriggersChanged?.Invoke();
            Changed?.Invoke(); // a rule's sentence names states and actions: a rename or a removal shows at once
        }, DispatcherPriority.Background);
    }

    private void OnActionRan(HotkeyAction action, RunChain? chain)
    {
        if (Paused || SessionOff) return;
        foreach (var rule in Settings.Rules.Where(IsActive).ToList())
            if (rule.AllTriggers.Any(t => AutomationTriggers.FiresOn(t, action, _sources))) StartFrom(rule, chain);
    }

    private void OnStateChanged(StateChange change)
    {
        if (Paused || SessionOff) return;
        foreach (var rule in Settings.Rules.Where(IsActive).ToList())
            if (rule.AllTriggers.Any(t => AutomationTriggers.FiresOn(t, change))) StartFrom(rule, change.Chain);
    }

    /// <summary>Starts a rule from something another rule may have caused: never from its own chain, and never past the chain limit.</summary>
    private void StartFrom(AutomationRule rule, RunChain? chain)
    {
        if (!RunChain.MayStart(chain, rule.Id, Timing.AutomationChainMax))
        {
            Log($"rule \"{rule.Name}\" not started: it would start itself again or make a chain of more than {Timing.AutomationChainMax} rules");
            return;
        }
        TryStart(rule, RunChain.After(chain, rule.Id));
    }

    private void OnActionsMoved(IReadOnlyDictionary<string, string> moved) =>
        Follow(rule =>
        {
            var changed = MovedActions.RewriteAll(rule.Actions, moved);
            return RewriteTriggers(rule, AutomationTriggers.ActionRuns, moved) | changed;
        });

    private void OnStatesMoved(IReadOnlyDictionary<string, string> moved) =>
        Follow(rule => RewriteTriggers(rule, AutomationTriggers.StateChanges, moved));

    private void Follow(Func<AutomationRule, bool> rewrite)
    {
        var changed = Settings.Rules.Aggregate(false, (any, rule) => rewrite(rule) | any);
        if (!changed) return;
        Settings.Save();
        Changed?.Invoke();
    }

    private static bool RewriteTriggers(AutomationRule rule, string kind, IReadOnlyDictionary<string, string> moved)
    {
        var first = MovedActions.Rewrite(rule.Trigger, kind, moved);
        var changed = first != rule.Trigger;
        rule.Trigger = first;
        for (var i = 0; i < rule.MoreTriggers.Count; i++)
        {
            var now = MovedActions.Rewrite(rule.MoreTriggers[i], kind, moved);
            changed |= now != rule.MoreTriggers[i];
            rule.MoreTriggers[i] = now;
        }
        return changed;
    }
}
