using System.Collections.ObjectModel;
using OmniControl.App.Services;
using Avalonia.Threading;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// The master list of everything OmniControl itself can do. A feature calls <see cref="Register"/> once per action; it then shows up
/// on the Shortcuts page (and in search), in the mouse button and middle-click pickers, and (for a level action) in the wheel gesture
/// picker, and is saved and registered as a hotkey automatically. Consumers read <see cref="Actions"/> and follow its changes.
/// </summary>
public sealed class HotkeyRegistry : IActionRegistry, IDisposable
{
    private static readonly DataStore Store = DataStores.Hotkeys;

    private readonly HotkeyService _service = new();
    private readonly Dictionary<string, StoredHotkey> _stored = Load();

    public ObservableCollection<HotkeyAction> Actions { get; } = new();

    /// <summary>Every saved binding was written (a key given, cleared or imported): the shortcut sets keep the current set in step with it.</summary>
    public event Action? BindingsSaved;

    private readonly List<HotkeyAction> _pickerCommands = new();

    /// <summary>What the action pickers list: every action, then the buttons of their lists (<see cref="HotkeyAction.Command"/>), which are not actions and never reach Shortcuts or a shortcut key.</summary>
    public IEnumerable<HotkeyAction> PickerActions => Actions.Concat(_pickerCommands);

    /// <summary>Adds a button to a picker list (see <see cref="PickerActions"/>).</summary>
    public void RegisterPickerCommand(HotkeyAction command)
    {
        _pickerCommands.Add(command);
        command.SectionPlace = PlaceOf(command.Section);
    }

    private IReadOnlyList<ActionSection> _pages = Array.Empty<ActionSection>();

    /// <summary>
    /// Hands over the sidebar pages as the sections of the Choose-the-action window (name, icon, order), read from the live page list; the actions
    /// registered before and after take their place from it, so registration order never matters.
    /// </summary>
    public void UseSections(IReadOnlyList<ActionSection> pages)
    {
        _pages = pages;
        foreach (var action in PickerActions) action.SectionPlace = PlaceOf(action.Section);
    }

    /// <summary>Where a page's section stands in the sidebar (1 = first); after all pages when it is not one.</summary>
    public int RankOf(string section) => PlaceOf(section)?.Rank ?? int.MaxValue;

    private ActionSection? PlaceOf(string section) => _pages.FirstOrDefault(p => p.Name == section) ?? ActionSections.Find(section);

    /// <summary>Asked before every action runs (see <see cref="IActionGate"/>); null = nothing is ever refused.</summary>
    public IActionGate? Gate { get; set; }

    /// <summary>Where problems are shown (the footer status line).</summary>
    public Action<string>? Report { get; set; }

    public HotkeyRegistry()
    {
        _service.Failed += gesture => Dispatcher.UIThread.Post(() =>
        {
            var names = string.Join(", ", Actions.Where(a => a.Gesture == gesture).Select(a => a.Title));
            Report?.Invoke($"Couldn't register {gesture} for {names}: another app is probably using it. Try another combination.");
        });
    }

    public void Register(HotkeyAction action)
    {
        if (_stored.TryGetValue(action.Id, out var saved)) action.Restore(saved);
        action.Changed += OnChanged;
        action.SectionPlace = PlaceOf(action.Section);
        Actions.Add(action);
        Rebind();
    }

    /// <summary>Registers several actions at once (one rebinding of the hotkeys).</summary>
    public void Register(IEnumerable<HotkeyAction> actions)
    {
        foreach (var action in actions)
        {
            if (_stored.TryGetValue(action.Id, out var saved)) action.Restore(saved);
            action.Changed += OnChanged;
            action.SectionPlace = PlaceOf(action.Section);
            Actions.Add(action);
        }
        Rebind();
    }

    public void Unregister(string id)
    {
        if (Find(id) is not { } action) return;
        action.Changed -= OnChanged;
        Actions.Remove(action);
        Rebind();
    }

    public HotkeyAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == RenamedActions.Id(id));

    public IReadOnlyList<string> SavedWithShortcut(string prefix) =>
        _stored.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Value.Gesture.Length > 0).Select(p => p.Key).ToList();

    public string? ProblemWith(string gesture, string id) => ConflictOf(gesture, id, Actions);

    /// <summary>
    /// The one rule for a shortcut given on purpose to one action (the preset manager's shortcut box): it must be readable and hold Ctrl, Alt
    /// or Win (<see cref="HotkeyService.ProblemWith"/>), and no other action may have it. Null when it can be given.
    /// </summary>
    public static string? ConflictOf(string gesture, string id, IEnumerable<HotkeyAction> actions) =>
        HotkeyService.ProblemWith(gesture)
        ?? ReservedKeys.ProblemWith(gesture)
        ?? (actions.FirstOrDefault(a => a.Id != id && a.Gesture == gesture) is { } other ? $"{gesture} is already the shortcut of \"{other.Title}\"." : null);

    /// <summary>
    /// Binds the suggested key of each of these actions that has one and no key yet. A key another action already has is not taken from it:
    /// that action is skipped. Returns the titles of the skipped actions.
    /// </summary>
    public IReadOnlyList<string> UseSuggested(IEnumerable<HotkeyAction> actions)
    {
        var skipped = new List<string>();
        foreach (var action in HotkeyDefaults.WithSuggestion(actions).Where(a => a.Gesture.Length == 0).ToList())
        {
            if (ConflictOf(action.SuggestedKey!, action.Id, Actions) is not null) skipped.Add(action.Title);
            else action.Gesture = action.SuggestedKey!;
        }
        return skipped;
    }

    /// <summary>Runs an action by id (for example from a mouse button). False when no such action is registered.</summary>
    public bool Run(string id)
    {
        var action = Find(id);
        if (action is null) return false;
        RunSoon(action);
        return true;
    }

    /// <summary>Runs an action by id with the settings the input chose for it (a program to start, how far a level moves, a power countdown).</summary>
    public bool Run(string id, RunSettings? settings)
    {
        var action = Find(id);
        if (action is null) return false;
        if (settings is null) RunSoon(action);
        else Dispatcher.UIThread.Post(() =>
        {
            if (Permitted(action)) action.RunWith(settings);
        }, DispatcherPriority.Send);
        return true;
    }

    /// <summary>
    /// Moves the level with this <see cref="HotkeyLevel.Id"/> up or down by <paramref name="percent"/> (for a mouse wheel gesture set to
    /// change it by that much), through the registered action that moves it that way. False when no such action is registered.
    /// </summary>
    public bool RunLevel(string levelId, bool up, int percent)
    {
        var action = Actions.FirstOrDefault(a => a.Level?.Id == levelId && a.LevelDirection > 0 == up);
        if (action is null) return false;
        Dispatcher.UIThread.Post(() =>
        {
            if (Permitted(action)) action.RunBy(percent);
        }, DispatcherPriority.Send);
        return true;
    }

    // Ahead of everything else queued (input, rendering, layout), so a pop-up or a volume step answers the key or wheel at once
    // even while a page is busy redrawing.
    private void RunSoon(HotkeyAction action) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (Permitted(action)) action.Run();
        }, DispatcherPriority.Send);

    /// <summary>The one gate every run passes: false (and one line in the status) when a block rule forbids the action now.</summary>
    private bool Permitted(HotkeyAction action)
    {
        if (Gate?.BlockReason(action) is not { } reason) return true;
        Report?.Invoke(reason);
        return false;
    }

    /// <summary>Every action's shortcut and device choice by id: the copy "Restore defaults" keeps.</summary>
    public Dictionary<string, StoredHotkey> ExportAll()
    {
        var all = new Dictionary<string, StoredHotkey>();
        foreach (var action in Actions) all[action.Id] = action.Export();
        return all;
    }

    /// <summary>Puts these shortcuts and device choices in place (an action not named has none), with one save and one rebinding.</summary>
    public void ImportAll(IReadOnlyDictionary<string, StoredHotkey> all)
    {
        foreach (var action in Actions)
        {
            action.Restore(all.TryGetValue(action.Id, out var saved) ? saved : HotkeyDefaults.Cleared);
            _stored[action.Id] = action.Export();
        }
        Save();
        Rebind();
    }

    /// <summary>Every saved binding by action id, also those of actions not registered now (a preset's shortcut): what a shortcut set keeps.</summary>
    public Dictionary<string, StoredHotkey> ExportStored() => new(_stored);

    /// <summary>Makes these bindings the saved ones (an action not named has none), with one save and one rebinding.</summary>
    public void ImportStored(IReadOnlyDictionary<string, StoredHotkey> all)
    {
        _stored.Clear();
        foreach (var (id, saved) in all) _stored[id] = saved;
        foreach (var action in Actions)
        {
            action.Restore(_stored.GetValueOrDefault(action.Id) ?? HotkeyDefaults.Cleared);
            _stored[action.Id] = action.Export();
        }
        Save();
        Rebind();
    }

    private void OnChanged(HotkeyAction changed)
    {
        if (ReservedKeys.ProblemWith(changed.Gesture) is { } reserved)
        {
            Report?.Invoke(reserved);
            changed.Gesture = string.Empty; // raises Changed again, which saves and rebinds
            return;
        }
        // A key combination can only belong to one action.
        if (changed.Gesture.Length > 0)
            foreach (var other in Actions.Where(a => a != changed && a.Gesture == changed.Gesture).ToList())
                other.Gesture = string.Empty;
        foreach (var a in Actions) _stored[a.Id] = a.Export();
        Save();
        Rebind();
    }

    private void Rebind() =>
        _service.Apply(Actions.Where(a => a.Gesture.Length > 0 && ReservedKeys.ProblemWith(a.Gesture) is null)
            .Select(a => (a.Gesture, (Action)(() => RunSoon(a)))));

    private static Dictionary<string, StoredHotkey> Load() =>
        RenamedActions.Migrate(AppDataStore.Load<Dictionary<string, StoredHotkey>>(Store) ?? new());

    private void Save()
    {
        AppDataStore.Save(Store, _stored);
        BindingsSaved?.Invoke();
    }

    public void Dispose() => _service.Dispose();
}
