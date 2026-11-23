using System.Collections.Specialized;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Shared.Scripts;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

// The live triggers: the state triggers follow the master list of states, "When an action
// runs" follows the one run path of HotkeyRegistry. Both lists are read live, so anything registered anywhere is a trigger at once, and a
// rename, a removal or a merge reaches the rules here. Loops: a rule never starts from a chain it is already in, and a chain is cut at
// Timing.AutomationChainMax (the minimum gap and the burst pause still apply on top).
public sealed partial class AutomationService
{
    private readonly IStateRegistry _states;
    private readonly ITriggerSources _sources;
    private readonly ProgramStates _programs;

    /// The lists the trigger picker reads changed (an action or a state came, went or was renamed): an open picker reads them again.
    public event Action? TriggersChanged;

    private sealed class Sources : ITriggerSources
    {
        private readonly IActionCatalogue _hotkeys;
        private readonly IActionRunner _run;
        private readonly IStateRegistry _states;
        private readonly ProgramStates _programs;

        public Sources(IActionCatalogue hotkeys, IActionRunner run, IStateRegistry states, ProgramStates programs) => (_hotkeys, _run, _states, _programs) = (hotkeys, run, states, programs);

        public IReadOnlyList<StateEntry> States => _states.States;
        public StateEntry? State(string id) => _states.Find(id);
        public StateValue? Now(string stateId) => _states.Now(stateId);
        public DateTime? ChangedAt(string stateId) => _states.ChangedAt(stateId);
        public IEnumerable<HotkeyAction> Actions => _hotkeys.Actions;
        public HotkeyAction? Action(string id) => _hotkeys.Find(id);
        public ActionSection? SectionOf(string section) => _hotkeys.SectionOf(section);
        public ProgramFacts? ProgramOf(string stateId) => _programs.FactsOf(stateId);
    }

    private void StartLive()
    {
        _states.Register(PowerStates.Make().Append(PausedState()).Append(new StateEntry(AutomationTriggers.IdleStateId, "PC idle time", AutomationTriggers.Clock, StateKind.Level,
            () => _idleMinutes is { } minutes ? StateValue.Of(minutes) : null, "Windows' time since the last keyboard or mouse input.",
            Max: AutomationTriggers.IdleMinutes.Max, Unit: "min", Keywords: "idle inactive away last input screen saver")));
        _run.Ran += OnActionRan;
        _states.ValueChanged += OnStateChanged;
        _hotkeys.Actions.CollectionChanged += OnActionsListChanged;
        _states.ListChanged += OnListsChanged;
        _hotkeys.Moved += OnActionsMoved;
        _states.Moved += OnStatesMoved;
        _watcher.DevicesSeen += OnUsbSeen;
        Changed += ShareWatched;
        ShareWatched();
        SyncPrograms();
    }

    /// The master list of states holds the programs the rules name; an active rule's programs are looked at (the rules' look reads them only then).
    private void SyncPrograms()
    {
        IEnumerable<string> ProgramsOf(IEnumerable<AutomationRule> rules) =>
            rules.SelectMany(r => r.AllTriggers).Select(AutomationTriggers.ProgramStateOf).OfType<string>().Distinct().ToList();
        _programs.Follow(ProgramsOf(Settings.Rules).ToList(), ProgramsOf(Settings.Rules.Where(Watches)).ToList());
    }

    /// Tells the master list of states which ones the active rules' triggers follow (a reading is then kept fresh for them).
    private void ShareWatched()
    {
        _states.Watch(nameof(AutomationService), Settings.Rules.Where(Watches).SelectMany(r => r.AllTriggers)
            .Select(MouseAction.Parse).Where(t => t.Id == AutomationTriggers.StateChanges).Select(t => t.Param));
        SyncUsb();
    }

    private void StopLive()
    {
        _run.Ran -= OnActionRan;
        _states.ValueChanged -= OnStateChanged;
        _hotkeys.Actions.CollectionChanged -= OnActionsListChanged;
        _states.ListChanged -= OnListsChanged;
        _hotkeys.Moved -= OnActionsMoved;
        _states.Moved -= OnStatesMoved;
        _watcher.DevicesSeen -= OnUsbSeen;
        _lookingAtUsb = false;
        Changed -= ShareWatched;
        _states.Watch(nameof(AutomationService), Array.Empty<string>());
        _states.Unregister(PowerStates.Ids.Append(PausedId).Append(AutomationTriggers.IdleStateId));
        _states.Unregister(_states.States.Where(s => s.Id.StartsWith(UsbPrefix, StringComparison.Ordinal)).Select(s => s.Id));
        _programs.Release();
    }

    private void NotifyPowerStates()
    {
        foreach (var id in PowerStates.Ids) _states.Notify(id);
    }

    /// The "Choose a trigger or state" list, on <paramref name="keep"/> (the stored trigger being edited; empty for none), without "I press a
    /// hotkey" unless <paramref name="withHotkey"/>. While a picker shows it, it is read again when a list or a state's value changes.
    public PickList TriggerList(string keep, bool withHotkey)
    {
        var programs = new ProgramSnapshot(_remembered().ToList(), AutomationWatcher.RunningPrograms); // read once for this window, never followed
        void Offer() => _programs.Offer(programs.Found, programs.Running);
        return new(AutomationTriggers.PickTitle, AutomationTriggers.SearchText, new LiveList(
            () => AutomationTriggers.Choices(_sources, keep, _programs.AddAsync).Where(c => withHotkey || c.Spec != AutomationTriggers.Hotkey).ToList(),
            h => { TriggersChanged += h; ValuesShown += h; programs.Ready += Offer; _programs.PickerOpened(); _usbPickers++; SyncUsb(); },
            h => { TriggersChanged -= h; ValuesShown -= h; programs.Ready -= Offer; _programs.PickerClosed(); _usbPickers--; SyncUsb(); }));
    }

    // Raised (once per batch) when a state's value changed, only while a picker shows the values: nothing listens otherwise.
    private event Action? ValuesShown;
    private bool _valuesPending;

    private void ShowValues()
    {
        if (ValuesShown is null || _valuesPending) return;
        _valuesPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _valuesPending = false;
            ValuesShown?.Invoke();
        }, DispatcherPriority.Background);
    }

    /// How a stored trigger reads in the rule's sentence, with the names the states and actions have now.
    public string TriggerPhrase(string stored) => AutomationTriggers.PhraseOf(stored, _sources);

    /// What a rule names that is not registered now: its trigger, and an action it does or blocks (a removed device, a deleted script).
    public (bool Trigger, bool Action) MissingOf(AutomationRule rule) =>
        (rule.AllTriggers.Any(t => AutomationTriggers.IsMissing(t, _sources)),
         rule.Actions.Concat(rule.OtherwiseActions).Any(a => AutomationSteps.ActionIdOf(a) is { } id && _hotkeys.Find(id) is null));

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
            // Which rules belong to a page follows its rule action's arrival; that decides what runs only while the user's rules are stopped.
            if (Paused || !UserRules) SyncTriggers();
            TriggersChanged?.Invoke();
            Changed?.Invoke(); // a rule's sentence names states and actions: a rename or a removal shows at once
        }, DispatcherPriority.Background);
    }

    private void OnActionRan(HotkeyAction action, RunChain? chain)
    {
        foreach (var rule in Settings.Rules.Where(r => Needs(r) && (!IsWhile(r) || r.Feature.Length > 0)).ToList())
        {
            var fires = (string t) => AutomationTriggers.FiresOn(t, action, _sources);
            if (!IsWhile(rule) && HasOtherwise(rule) && FiresAny(rule, fires))
            {
                if (TopHoldsWhenFired(rule, fires, NewContext())) StartFrom(rule, chain);
                else StartOtherwiseFrom(rule, chain);
            }
            else if (FiresWith(rule, fires)) StartFrom(rule, chain);
        }
    }

    private void OnStateChanged(StateChange change)
    {
        ShowValues();
        if (SessionOff) return;
        StateSeen?.Invoke();
        foreach (var rule in Settings.Rules.Where(r => Needs(r) && !IsWhile(r)).ToList())
        {
            var fires = (string t) => AutomationTriggers.FiresOn(t, change);
            if (HasOtherwise(rule) && FiresAny(rule, fires))
            {
                if (TopHoldsWhenFired(rule, fires, NewContext())) StartFromState(rule, change.Chain);
                else StartOtherwiseFrom(rule, change.Chain);
            }
            else if (FiresWith(rule, fires)) StartFromState(rule, change.Chain);
        }
        if (change.Entry.OneShot)
        {
            var recorded = false;
            foreach (var rule in Settings.Rules.Where(r => Needs(r) && r.Feature.Length > 0).ToList())
                if (FiresWith(rule, t => AutomationTriggers.FiresOn(t, change)))
                {
                    rule.LastRan = DateTime.Now;
                    recorded = true;
                }
            if (recorded)
            {
                Settings.Save();
                Changed?.Invoke();
            }
        }
        LookAtHeld();
    }

    /// Starts a rule from something another rule may have caused: never from its own chain, and never past the chain limit. False when it did not start.
    private bool StartFrom(AutomationRule rule, RunChain? chain)
    {
        if (!RunChain.MayStart(chain, rule.Id, Timing.AutomationChainMax))
        {
            Log($"rule \"{rule.Name}\" not started: it would start itself again or make a chain of more than {Timing.AutomationChainMax} rules");
            return false;
        }
        return TryStart(rule, RunChain.After(chain, rule.Id));
    }

    /// Starts a rule's Otherwise steps: its trigger fired but its top line did not hold. Same chain limit as a normal start.
    private bool StartOtherwiseFrom(AutomationRule rule, RunChain? chain)
    {
        if (!RunChain.MayStart(chain, rule.Id, Timing.AutomationChainMax))
        {
            Log($"rule \"{rule.Name}\" not started: it would start itself again or make a chain of more than {Timing.AutomationChainMax} rules");
            return false;
        }
        return TryStart(rule, RunChain.After(chain, rule.Id), otherwise: true);
    }

    private void OnActionsMoved(IReadOnlyDictionary<string, string> moved) =>
        Follow(rule =>
        {
            var changed = MovedActions.RewriteAll(rule.Actions, moved) | MovedActions.RewriteAll(rule.OtherwiseActions, moved);
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
