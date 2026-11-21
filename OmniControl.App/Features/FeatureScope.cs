using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features;

/// One running feature module's share of the master lists. The context it is given (<see cref="Context"/>) notes what the module adds through it
/// (actions and picker buttons, states and their watchers, hardware watches, preset groups, OSD elements, import hooks, shut-down steps), so a
/// feature switched off live (<see cref="FeatureSwitches"/>) runs its own shut-down steps, in their order, and then has whatever is still listed taken out.
public sealed class FeatureScope
{
    private readonly ScopedActions _actions;
    private readonly ScopedStates _states;
    private readonly ScopedWatches _watches;
    private readonly ScopedPresets _presets;
    private readonly ScopedOsd _osd;

    /// <param name="shared">The context every module shares.</param>
    /// <param name="unwatch">Takes a hardware watch out by id.</param>
    /// <param name="ungroup">Takes a preset group out by id.</param>
    /// <param name="undeclare">Takes back an OSD element by name.</param>
    public FeatureScope(FeatureContext shared, Action<string> unwatch, Action<string> ungroup, Action<string> undeclare)
    {
        _actions = new ScopedActions(shared.Actions);
        _states = new ScopedStates(shared.States);
        _watches = new ScopedWatches(shared.Hardware, unwatch);
        _presets = new ScopedPresets(shared.Presets, ungroup);
        _osd = new ScopedOsd(shared.Osd, undeclare);
        Context = shared with
        {
            Actions = _actions, States = _states, Hardware = _watches, Presets = _presets, Osd = _osd,
            Imports = shared.Imports.Owned(), Shutdown = shared.Shutdown.Owned(),
        };
    }

    public FeatureContext Context { get; }

    /// The feature was switched off: its shut-down steps run, then what it still has in the master lists goes.
    public void Stop()
    {
        Context.Shutdown.RunOwn();
        Context.Imports.Forget();
        _actions.Forget();
        _states.Forget();
        _watches.Forget();
        _presets.Forget();
        _osd.Forget();
    }

    private sealed class ScopedActions(IActionRegistry inner) : IActionRegistry
    {
        private readonly HashSet<string> _ids = new();

        public ObservableCollection<HotkeyAction> Actions => inner.Actions;
        public IEnumerable<HotkeyAction> PickerActions => inner.PickerActions;
        public HotkeyAction? Find(string id) => inner.Find(id);
        public ActionSection? SectionOf(string section) => inner.SectionOf(section);
        public HotkeyAction? OwnerOf(string gesture, string exceptId) => inner.OwnerOf(gesture, exceptId);
        public event Action? BindingsSaved { add => inner.BindingsSaved += value; remove => inner.BindingsSaved -= value; }
        public event Action<IReadOnlyDictionary<string, string>>? Moved { add => inner.Moved += value; remove => inner.Moved -= value; }
        public IReadOnlyList<string> SavedWithShortcut(string prefix) => inner.SavedWithShortcut(prefix);
        public string? ProblemWith(string gesture, string id) => inner.ProblemWith(gesture, id);
        public void Move(IReadOnlyDictionary<string, string> moved) => inner.Move(moved);

        public void Register(HotkeyAction action)
        {
            _ids.Add(action.Id);
            inner.Register(action);
        }

        public void Register(IEnumerable<HotkeyAction> actions)
        {
            var list = actions.ToList();
            _ids.UnionWith(list.Select(a => a.Id));
            inner.Register(list);
        }

        public void RegisterPickerCommand(HotkeyAction command)
        {
            _ids.Add(command.Id);
            inner.RegisterPickerCommand(command);
        }

        public void Unregister(string id)
        {
            _ids.Remove(id);
            inner.Unregister(id);
        }

        public void Forget()
        {
            foreach (var id in _ids) inner.Unregister(id);
            _ids.Clear();
        }
    }

    private sealed class ScopedStates(IStateRegistry inner) : IStateRegistry
    {
        private readonly HashSet<string> _ids = new(), _watchers = new();

        public IReadOnlyList<StateEntry> States => inner.States;
        public event Action? ListChanged { add => inner.ListChanged += value; remove => inner.ListChanged -= value; }
        public event Action<StateChange>? ValueChanged { add => inner.ValueChanged += value; remove => inner.ValueChanged -= value; }
        public event Action<IReadOnlyDictionary<string, string>>? Moved { add => inner.Moved += value; remove => inner.Moved -= value; }
        public event Action<StateEntry>? LivenessChanged { add => inner.LivenessChanged += value; remove => inner.LivenessChanged -= value; }
        public event Action? WatchedChanged { add => inner.WatchedChanged += value; remove => inner.WatchedChanged -= value; }
        public StateEntry? Find(string id) => inner.Find(id);
        public StateValue? Now(string id) => inner.Now(id);
        public void Move(IReadOnlyDictionary<string, string> moved) => inner.Move(moved);
        public void Notify(string id) => inner.Notify(id);
        public void Notify(IReadOnlyList<string> ids) => inner.Notify(ids);
        public void NotifyLiveness(string id) => inner.NotifyLiveness(id);
        public bool IsWatched(string id) => inner.IsWatched(id);

        public void Register(StateEntry entry)
        {
            _ids.Add(entry.Id);
            inner.Register(entry);
        }

        public void Register(IEnumerable<StateEntry> entries)
        {
            var list = entries.ToList();
            _ids.UnionWith(list.Select(e => e.Id));
            inner.Register(list);
        }

        public void Unregister(string id)
        {
            _ids.Remove(id);
            inner.Unregister(id);
        }

        public void Unregister(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            _ids.ExceptWith(list);
            inner.Unregister(list);
        }

        public void Watch(string watcher, IEnumerable<string> ids)
        {
            _watchers.Add(watcher);
            inner.Watch(watcher, ids);
        }

        public void Forget()
        {
            inner.Unregister(_ids.ToList());
            foreach (var watcher in _watchers) inner.Watch(watcher, Array.Empty<string>());
            _ids.Clear();
            _watchers.Clear();
        }
    }

    private sealed class ScopedWatches(IHardwareWatches inner, Action<string> unwatch) : IHardwareWatches
    {
        private readonly HashSet<string> _ids = new();

        public event Action<PowerSignal>? Power { add => inner.Power += value; remove => inner.Power -= value; }
        public ScreenLight Screens => inner.Screens;
        public event Action? ScreensChanged { add => inner.ScreensChanged += value; remove => inner.ScreensChanged -= value; }
        public void Signal(HardwareSignal signal, string? source = null) => inner.Signal(signal, source);
        public void Request(string id) => inner.Request(id);
        public bool RefreshThen(string id, Action then) => inner.RefreshThen(id, then);

        public void Register(HardwareWatch watch)
        {
            _ids.Add(watch.Id);
            inner.Register(watch);
        }

        public void Forget()
        {
            foreach (var id in _ids) unwatch(id);
            _ids.Clear();
        }
    }

    private sealed class ScopedPresets(IPresetGroups inner, Action<string> ungroup) : IPresetGroups
    {
        private readonly List<string> _ids = new();

        public void Register(PresetGroup group)
        {
            inner.Register(group);
            _ids.Add(group.Id);
        }

        public void Register(IEnumerable<PresetGroup> groups)
        {
            foreach (var group in groups) Register(group);
        }

        public void Forget()
        {
            foreach (var id in _ids) ungroup(id);
            _ids.Clear();
        }
    }

    private sealed class ScopedOsd(IOsd inner, Action<string> undeclare) : IOsd
    {
        private readonly HashSet<string> _names = new();

        public event Action<int, bool>? VolumeWatched { add => inner.VolumeWatched += value; remove => inner.VolumeWatched -= value; }
        public event Action<int, bool>? MicrophoneWatched { add => inner.MicrophoneWatched += value; remove => inner.MicrophoneWatched -= value; }
        public void NoteOwnChange(string element) => inner.NoteOwnChange(element);
        public void ShowVolume(int value, bool muted) => inner.ShowVolume(value, muted);
        public void Show(string element, int value) => inner.Show(element, value);
        public void Show(string element, int value, string label) => inner.Show(element, value, label);
        public void ShowMessage(string element, OsdMessage message, string? program = null) => inner.ShowMessage(element, message, program);
        public void ShowDeviceNote(OsdMessage message) => inner.ShowDeviceNote(message);
        public void WatchVolumeOf(Func<string?> device) => inner.WatchVolumeOf(device);
        public void RetargetVolume() => inner.RetargetVolume();

        public void Declare(string name, OsdLevelIcon icon)
        {
            _names.Add(name);
            inner.Declare(name, icon);
        }

        public void DeclareText(string name)
        {
            _names.Add(name);
            inner.DeclareText(name);
        }

        public void Forget()
        {
            foreach (var name in _names) undeclare(name);
            _names.Clear();
        }
    }
}
