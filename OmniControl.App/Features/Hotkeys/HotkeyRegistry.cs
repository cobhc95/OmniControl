using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Hotkeys;

/// The master list of everything OmniControl itself can do. A feature calls <see cref="Register"/> once per action; it then shows up
/// in the Actions and Hotkeys window (and in search), in the mouse button and middle-click pickers, and (for a level action) in the wheel gesture
/// picker, and is saved and registered as a hotkey automatically. Consumers read <see cref="Actions"/> and follow its changes.
public sealed partial class HotkeyRegistry : IActionRegistry, IActionRunner, IDisposable
{
    private static readonly DataStore Store = DataStores.Hotkeys;

    private readonly HotkeyService _service = new();
    private readonly IHardwareWatches _hardware;
    private readonly Dictionary<string, StoredHotkey> _stored = Load();
    private readonly bool _fresh; // nothing was saved before this run: a first start

    public ObservableCollection<HotkeyAction> Actions { get; } = new();

    /// Every saved binding was written (a key given, cleared or imported): the shortcut sets keep the current set in step with it.
    public event Action? BindingsSaved;

    private readonly List<HotkeyAction> _pickerCommands = new();

    /// What the action pickers list: every action, then the buttons of their lists (<see cref="HotkeyAction.Command"/>), which are not actions and never get a shortcut key.
    public IEnumerable<HotkeyAction> PickerActions => Actions.Concat(_pickerCommands);

    /// Adds a button to a picker list (see <see cref="PickerActions"/>).
    public void RegisterPickerCommand(HotkeyAction command)
    {
        _pickerCommands.Add(command);
        command.SectionPlace = PlaceOf(command.Section);
    }

    private IReadOnlyList<ActionSection> _pages = Array.Empty<ActionSection>();

    /// Hands over the sidebar pages as the sections of the Choose-the-action window (name, icon, order), read from the live page list; the actions
    /// registered before and after take their place from it, so registration order never matters.
    public void UseSections(IReadOnlyList<ActionSection> pages)
    {
        _pages = pages;
        foreach (var action in PickerActions) action.SectionPlace = PlaceOf(action.Section);
    }

    private ActionSection? PlaceOf(string section) => _pages.FirstOrDefault(p => p.Name == section) ?? ActionSections.Find(section);

    /// The section (name, icon, place) of a page or an extra section; null when there is none by that name.
    public ActionSection? SectionOf(string section) => PlaceOf(section);

    /// Every section in order: the sidebar pages, then the extra ones (their icons are what the tray flyout's tiles may show).
    public IReadOnlyList<ActionSection> Sections => _pages.Concat(ActionSections.Extras).ToList();

    private readonly ActionGateSlot? _gate;
    private readonly IActionSettings? _settings;
    private readonly Func<IStateReader?> _states;
    private readonly Action<string> _report;

    /// The action's own settings (see <see cref="IActionSettings.OptionsOf"/>); empty when it takes none or no settings are known.
    public IReadOnlyList<ActionOption> OptionsOf(HotkeyAction action) => _settings?.OptionsOf(action) ?? Array.Empty<ActionOption>();

    /// What an input that chose these settings runs the action with. An input that keeps no copy of its own (<see cref="OptionValues.OwnKey"/> unset, none of
    /// the action's own settings stored: every binding made before the action had defaults) runs it with the action's defaults, so editing them
    /// changes what it does.
    private RunSettings? Resolve(HotkeyAction action, RunSettings? settings)
    {
        if (_settings is not { } known || settings is null) return settings;
        var chosen = settings.Options ?? OptionValues.None;
        var keeps = chosen.Has(OptionValues.OwnKey) || known.OptionsOf(action).Any(o => chosen.Has(o.Key));
        return keeps ? settings : known.RunOf(new OptionValues(action.Defaults));
    }

    /// Runs the action as a key or a rule does: with its defaults once it has any, else as it is.
    private void RunByDefaults(HotkeyAction action)
    {
        if (_settings is { } known && action.Defaults.Count > 0) action.RunWith(known.RunOf(new OptionValues(action.Defaults)));
        else action.Run();
    }

    /// <param name="hardware">The master list of hardware watches: an action whose picked device is missing makes its watch refresh once before it is run.</param>
    /// <param name="report">Where problems are shown (the footer status line).</param>
    /// <param name="settings">What the actions' own settings are and mean (<see cref="IActionSettings"/>); null = none are used.</param>
    /// <param name="states">Answers the state an On or Off action is linked to (<see cref="HotkeyAction.StateId"/>); read when asked, as the state list is made after this registry.</param>
    /// <param name="gate">The slot the one <see cref="IActionGate"/> is attached to; null = nothing is ever refused.</param>
    public HotkeyRegistry(IHardwareWatches hardware, Action<string> report, IActionSettings? settings = null, Func<IStateReader?>? states = null, ActionGateSlot? gate = null)
    {
        _hardware = hardware;
        _report = report;
        _settings = settings;
        _states = states ?? (() => null);
        _gate = gate;
        _fresh = _stored.Count == 0;
        _service.Failed += gesture => Dispatcher.UIThread.Post(() =>
        {
            var names = string.Join(", ", Actions.Where(a => a.Keys.Contains(gesture)).Select(a => a.Title));
            _report($"Couldn't register {gesture} for {names}: another app is probably using it. Try another combination.");
        });
    }

    public void Register(HotkeyAction action)
    {
        Add(action);
        Rebind();
        if (SettleSuggestedKeys()) Save();
    }

    /// Registers several actions at once (one rebinding of the hotkeys).
    public void Register(IEnumerable<HotkeyAction> actions)
    {
        foreach (var action in actions) Add(action);
        Rebind();
        if (SettleSuggestedKeys()) Save();
    }

    // The saved keys, else (first time only) its default key unless the key is not allowed or another action has it already.
    private void Add(HotkeyAction action)
    {
        if (_stored.TryGetValue(action.Id, out var saved)) action.Restore(saved);
        else if (action.DefaultKey is { } key && ConflictOf(key, action.Id, Actions) is null)
        {
            action.Restore(HotkeyDefaults.For(key));
            _stored[action.Id] = action.Export();
        }
        action.Changed += OnChanged;
        action.SectionPlace = PlaceOf(action.Section);
        Actions.Add(action);
    }

    /// The default key combination of the action with this id, or null.
    public string? DefaultKeyOf(string id) => Find(id)?.DefaultKey;

    public void Unregister(string id)
    {
        _pickerCommands.RemoveAll(c => c.Id == id);
        if (Find(id) is not { } action) return;
        action.Changed -= OnChanged;
        Actions.Remove(action);
        Rebind();
    }

    public HotkeyAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

    public string? ProblemWith(string gesture, string id) => ConflictOf(gesture, id, Actions);

    /// The one rule for a shortcut given on purpose to one action (the preset manager's shortcut box): it must be readable and hold Ctrl, Alt
    /// or Win (<see cref="HotkeyService.ProblemWith"/>), and no other action may have it. Null when it can be given.
    public static string? ConflictOf(string gesture, string id, IEnumerable<HotkeyAction> actions) =>
        HotkeyService.ProblemWith(gesture)
        ?? ReservedKeys.ProblemWith(gesture)
        ?? (OwnerOf(gesture, id, actions) is { } other ? $"{gesture} is already the shortcut of \"{other.Title}\"." : null);

    /// The action other than <paramref name="exceptId"/> that has this key, or null.
    public static HotkeyAction? OwnerOf(string gesture, string exceptId, IEnumerable<HotkeyAction> actions) =>
        actions.FirstOrDefault(a => a.Id != exceptId && a.Keys.Contains(gesture));

    /// The action other than <paramref name="exceptId"/> that has this key in the set in use, or null.
    public HotkeyAction? OwnerOf(string gesture, string exceptId) => OwnerOf(gesture, exceptId, Actions);

    /// Runs an action by id (for example from a mouse button). False when no such action is registered.
    public bool Run(string id)
    {
        var action = Find(id);
        if (action is null) return false;
        RunSoon(action);
        return true;
    }

    /// Runs an action by id with the settings the input chose for it (a program to start, how far a level moves, a power countdown).
    public bool Run(string id, RunSettings? settings)
    {
        var action = Find(id);
        if (action is null) return false;
        if (settings is null) RunSoon(action);
        else
        {
            var chain = _chain.Value;
            var resolved = Resolve(action, settings)!;
            Dispatcher.UIThread.Post(() => RunNow(action, chain, a => a.RunWith(resolved)), DispatcherPriority.Send);
        }
        return true;
    }

    /// Starts an action that lasts while its input is held (<see cref="HotkeyAction.RunHeld"/>) and returns what ends it; null when the action does not last with these settings.
    public Action? RunHeld(string id, RunSettings settings)
    {
        if (Find(id) is not { RunHeld: { } start } action) return null;
        var chain = _chain.Value;
        // A refused action still answers "it lasts" with nothing to end, so the input does not fall back to running it once.
        if (!PermittedFromAnyThread(action, chain)) return () => { };
        Action? stop = null;
        var resolved = Resolve(action, settings)!;
        if (Guard.Run($"Action {action.Id}", () => stop = start(resolved)) is not null)
        {
            _report(Guard.SeeLog($"Couldn't run {action.Title}."));
            return () => { };
        }
        if (stop is not null) Dispatcher.UIThread.Post(() => RaiseRan(action, chain), DispatcherPriority.Send);
        return stop;
    }

    /// The gate's answer for a caller that may be on the mouse hook's thread: asked on the UI thread, waiting only <see cref="Timing.GateAskWait"/> (no answer in time means no).
    private bool PermittedFromAnyThread(HotkeyAction action, RunChain? chain)
    {
        if (Dispatcher.UIThread.CheckAccess()) return Permitted(action, chain);
        var ask = Dispatcher.UIThread.InvokeAsync(() => Permitted(action, chain), DispatcherPriority.Send).GetTask();
        return ask.Wait(Timing.GateAskWait) && ask.Result;
    }

    /// Moves the level with this <see cref="HotkeyLevel.Id"/> up or down by <paramref name="percent"/> (for a mouse wheel gesture set to
    /// change it by that much), through the registered action that moves it that way. False when no such action is registered.
    public bool RunLevel(string levelId, bool up, int percent)
    {
        var action = Actions.FirstOrDefault(a => a.Level?.Id == levelId && a.LevelDirection > 0 == up);
        if (action is null) return false;
        var chain = _chain.Value;
        Dispatcher.UIThread.Post(() => RunNow(action, chain, a => a.RunBy(percent)), DispatcherPriority.Send);
        return true;
    }

    // Ahead of everything else queued (input, rendering, layout), so a pop-up or a volume step answers the key or wheel at once
    // even while a page is busy redrawing.
    private void RunSoon(HotkeyAction action)
    {
        var chain = _chain.Value; // read on the calling thread: a rule's worker carries its chain here
        Dispatcher.UIThread.Post(() => RunNow(action, chain, RunByDefaults), DispatcherPriority.Send);
    }

    /// THE run path: every action that runs (a key, a mouse button, a gesture, a rule, Run now) passes here on the UI thread. It asks the
    /// gate, runs the action with the chain of rules that caused it in force (so what the action does, and the state changes it makes,
    /// carry it on), then raises <see cref="Ran"/>.
    private void RunNow(HotkeyAction action, RunChain? chain, Action<HotkeyAction> run, bool retried = false, bool stateRead = false)
    {
        // A picked device that is not listed may only be unseen (a monitor after Remote Desktop): its watch looks again once, off this thread, and the action then runs once more
        // (at the same priority as the refresh's own posts to the window, so it follows them).
        if (!retried && action.HasMissingPick
            && _hardware.RefreshThen(action.HardwareWatch!, () => Dispatcher.UIThread.Post(() => RunNow(action, chain, run, retried: true)))) return;
        if (!Permitted(action, chain)) return;
        if (SkipsOrWaitsForState(action, chain, run, stateRead)) return;
        var outer = _chain.Value;
        _chain.Value = chain;
        try
        {
            // The one guard for every caller: a broken action is logged and said calmly, and never takes the app down.
            if (Guard.Run($"Action {action.Id}", () => run(action)) is not null)
            {
                _report(Guard.SeeLog($"Couldn't run {action.Title}."));
                return;
            }
        }
        finally
        {
            _chain.Value = outer;
        }
        RaiseRan(action, chain);
    }

    /// "Set to a state": an On or Off action linked to a state does nothing when the state is already the target. A state not known is read again once
    /// when its owner can (the run then continues from here); still not known, the action runs and says so. True when this run ends here.
    internal bool SkipsOrWaitsForState(HotkeyAction action, RunChain? chain, Action<HotkeyAction> run, bool stateRead)
    {
        if (action.StateId is not { } stateId || _states() is not { } reader || StateTarget.Wanted(action.FamilyRole) is not { } wanted) return false;
        switch (StateTarget.Decide(action.FamilyRole, reader.Read(stateId)))
        {
            case StateVerdict.AlreadyThere:
                _report($"{action.Family?.Title ?? action.Title} is already {(wanted ? "on" : "off")}.");
                return true;
            case StateVerdict.Unknown when !stateRead && reader.Refresh(stateId, () => RunNow(action, chain, run, retried: true, stateRead: true)):
                return true;
            case StateVerdict.Unknown:
                _report($"Not sure whether {action.Family?.Title ?? action.Title} is {(wanted ? "on" : "off")}, so it was sent anyway.");
                return false;
            default:
                return false;
        }
    }

    private void RaiseRan(HotkeyAction action, RunChain? chain)
    {
        if (Ran is not { } ran) return;
        foreach (var handler in ran.GetInvocationList().Cast<Action<HotkeyAction, RunChain?>>())
            Guard.Run($"Ran handler for {action.Id}", () => handler(action, chain));
    }

    private readonly AsyncLocal<RunChain?> _chain = new();

    /// The chain of rules whose run is going on in the calling code (null: a person or Windows started it).
    public RunChain? Chain => _chain.Value;

    /// Puts <paramref name="chain"/> in force for the rest of the calling async flow (a rule's worker, before it runs its steps).
    public void RunWithin(RunChain chain) => _chain.Value = chain;

    /// Raised on the UI thread after an action ran, with the chain of rules that caused it (null: none).
    public event Action<HotkeyAction, RunChain?>? Ran;

    /// Actions whose ids changed (old id to new id): whoever stored an old id (a button, a gesture, a rule) moves to the new one.
    public event Action<IReadOnlyDictionary<string, string>>? Moved;

    /// Says that the actions with these old ids are now the ones with the new ids (a merged smart device's actions).
    public void Move(IReadOnlyDictionary<string, string> moved)
    {
        if (moved.Count > 0) Moved?.Invoke(moved);
    }

    /// The one gate every run passes: false (and one line in the status) when a block rule forbids the action now, or a rule, a chain or another
    /// PC (<paramref name="chain"/> set) runs a <see cref="HotkeyAction.PersonOnly"/> action.
    internal bool Permitted(HotkeyAction action, RunChain? chain)
    {
        var reason = action.PersonOnly && chain is not null
            ? $"{action.Title} runs only from a key, a mouse button or a click, not from a rule or another PC."
            : _gate?.BlockReason(action);
        if (reason is null) return true;
        _report(reason);
        return false;
    }

    /// Every action's shortcut and device choice by id: the copy "Restore defaults" keeps.
    public Dictionary<string, StoredHotkey> ExportAll()
    {
        var all = new Dictionary<string, StoredHotkey>();
        foreach (var action in Actions) all[action.Id] = action.Export();
        return all;
    }

    /// Puts these shortcuts and device choices in place (an action not named has none), with one save and one rebinding.
    public void ImportAll(IReadOnlyDictionary<string, StoredHotkey> all)
    {
        foreach (var action in Actions)
        {
            action.Restore(all.TryGetValue(action.Id, out var saved) ? saved : HotkeyDefaults.Cleared);
            _stored[action.Id] = action.Export();
        }
        AdoptSuggestedKeys();
        SettleSuggestedKeys();
        Save();
        Rebind();
    }

    /// Every saved binding by action id, also those of actions not registered now (a preset's shortcut): what a shortcut set keeps.
    public Dictionary<string, StoredHotkey> ExportStored() => new(_stored);

    /// Makes these bindings the saved ones (an action not named has none), with one save and one rebinding.
    public void ImportStored(IReadOnlyDictionary<string, StoredHotkey> all)
    {
        TakeStored(all);
        Save();
        Rebind();
    }

    /// Reads hotkeys.json again and puts its bindings in force (an import from another PC wrote it): one rebinding, and neither the file nor the
    /// shortcut sets are written (the sets' own file arrived with it and is reloaded by its owner).
    public bool ReloadFromDisk()
    {
        TakeStored(Load());
        Rebind();
        return true;
    }

    private void TakeStored(IReadOnlyDictionary<string, StoredHotkey> all)
    {
        _stored.Clear();
        foreach (var (id, saved) in Unique(new Dictionary<string, StoredHotkey>(all))) _stored[id] = saved;
        foreach (var action in Actions)
        {
            action.Restore(_stored.GetValueOrDefault(action.Id) ?? HotkeyDefaults.Cleared);
            _stored[action.Id] = action.Export();
        }
        SettleSuggestedKeys();
    }

    private void OnChanged(HotkeyAction changed)
    {
        if (changed.Keys.Select(ReservedKeys.ProblemWith).FirstOrDefault(p => p is not null) is { } reserved)
        {
            _report(reserved);
            changed.SetKeys(changed.Keys.Where(k => ReservedKeys.ProblemWith(k) is null)); // raises Changed again, which saves and rebinds
            return;
        }
        // A key combination can only belong to one action.
        foreach (var other in Actions.Where(a => a != changed && a.Keys.Intersect(changed.Keys).Any()).ToList())
            other.SetKeys(other.Keys.Except(changed.Keys));
        TurnOffByHand(changed);
        foreach (var a in Actions) _stored[a.Id] = a.Export();
        Save();
        Rebind();
    }

    private bool _held, _paused, _stopped;

    /// "Hotkeys and automation" switched off (<see cref="HotkeysSwitch.Use"/>): no key at all is registered with Windows, not even a
    /// <see cref="HotkeyAction.NeverBlock"/> one. The keys stay saved and on their actions.
    public void StopKeys(bool stopped)
    {
        if (stopped == _stopped) return;
        _stopped = stopped;
        Rebind();
    }

    /// The hotkeys master switch (<see cref="HotkeysSwitch"/>): while paused, no key is registered with Windows except those of
    /// <see cref="HotkeyAction.NeverBlock"/> actions. The keys stay saved and on their actions. Mouse buttons and rules run through other paths and are not paused.
    public void PauseKeys(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Rebind();
    }

    /// While held, only the keys of the actions that must always work (<see cref="HotkeyAction.NeverBlock"/>) stay registered with Windows,
    /// so any other key combination reaches the window that asked (the Choose-the-action window's search box and hotkey boxes, where a pressed
    /// key is searched for or captured) instead of running its action. The emergency key has its own registration and is never held. Letting go registers them again.
    public void Hold(bool held)
    {
        if (held == _held) return;
        _held = held;
        Rebind();
    }

    private void Rebind() =>
        _service.Apply(Actions.Where(a => !_stopped && (!(_held || _paused) || a.NeverBlock)).SelectMany(a => a.Keys.Where(k => ReservedKeys.ProblemWith(k) is null).Select(k => (k, (Action)(() => RunSoon(a))))));

    private static Dictionary<string, StoredHotkey> Load() =>
        Unique(AppDataStore.Load<Dictionary<string, StoredHotkey>>(Store) ?? new());

    /// The bindings with every key on one action only (the first action that has it keeps it): a merged backup or a file from before an
    /// action could have several keys may hold the same key twice.
    internal static Dictionary<string, StoredHotkey> Unique(Dictionary<string, StoredHotkey> all)
    {
        var taken = new HashSet<string>();
        var result = new Dictionary<string, StoredHotkey>();
        foreach (var (id, saved) in all)
        {
            var keys = saved.Keys.Where(taken.Add).ToList();
            result[id] = saved with { Gesture = keys.FirstOrDefault() ?? string.Empty, More = keys.Count > 1 ? keys.Skip(1).ToList() : null };
        }
        return result;
    }

    private void Save()
    {
        AppDataStore.SaveSoon(Store, _stored);
        BindingsSaved?.Invoke();
    }

    public void Dispose() => _service.Dispose();
}
