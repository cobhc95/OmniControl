using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Hotkeys;

/// <summary>What gets saved for one hotkey.</summary>
public sealed record StoredHotkey(string Gesture, bool UseSelected, List<string> DeviceIds);

/// <summary>
/// A level that a pair of actions moves up and down (volume, brightness...). <see cref="Id"/> is stored by the gestures that drive it
/// (gestures.json), so it never changes; <see cref="Name"/> is what the gesture picker calls it.
/// </summary>
public sealed record HotkeyLevel(string Id, string Name);

/// <summary>
/// A bindable hotkey, and the master entry for everything OmniControl itself can do. Every feature registers its actions through
/// <see cref="HotkeyRegistry"/>; the Shortcuts page, search, the mouse button and gesture pickers all read them from there.
/// Pass a device source when the action targets variable devices (monitors...): the user can then
/// choose all devices or a hand-picked subset.
/// </summary>
public sealed partial class HotkeyAction : ObservableObject, Pages.IEditorItem
{
    string Pages.IEditorItem.Label => Title;

    private readonly Action<IReadOnlyList<string>?> _run;
    private readonly Action<IReadOnlyList<string>?, int>? _stepBy;
    private readonly Func<IReadOnlyList<HotkeyDevice>>? _deviceSource;
    private readonly string _allText;
    private readonly string _noun;
    private readonly HashSet<string> _selectedIds = new();
    private bool _loading;
    private bool _syncing;

    /// <summary>How much a level action (volume, brightness...) changes per press, in percent, when nothing else is asked for.</summary>
    public const int DefaultStep = 5;

    public string Id { get; }
    public string Title { get; }

    /// <summary>The feature the action belongs to: the title of the page that registers it.</summary>
    public string Category { get; }

    /// <summary>Optional part of the feature ("Hardware", "Software").</summary>
    public string? Group { get; }

    /// <summary>Optional part of the group ("Colour (RGB and gamma)"): the Shortcuts page folds it into its own panel under the group.</summary>
    public string? Subgroup { get; }

    /// <summary>Category and group as one line ("Displays - Hardware"): the panel the Shortcuts page puts the action in.</summary>
    public string Panel { get; }

    /// <summary>Category, group and subgroup as one line ("Displays - Hardware - Presets"): what every list shows as the action's category.</summary>
    public string Heading { get; }

    public string Keywords { get; }

    /// <summary>The level this action moves, for a level action made with <see cref="ForLevel"/>; null for any other action.</summary>
    public HotkeyLevel? Level { get; private init; }

    /// <summary>+1 when this level action raises its level, -1 when it lowers it.</summary>
    public int LevelDirection { get; private init; }

    public bool HasDevices => _deviceSource is not null;

    /// <summary>"All displays" first, then one chip per device.</summary>
    public ObservableCollection<ScopeChip> Chips { get; } = new();

    /// <summary>Like "Ctrl+Alt+Up". Empty means not bound.</summary>
    [ObservableProperty] private string _gesture = string.Empty;

    [ObservableProperty] private bool _useSelected;

    /// <summary>
    /// Why the action does nothing right now (a mode or device it needs is missing), set and cleared live by the feature that owns it;
    /// null = it works. The Shortcuts page shows it on the action; the key can still be bound.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUnavailable))] private string? _unavailableReason;

    public bool IsUnavailable => UnavailableReason is not null;

    /// <summary>True while the hotkey box waits for the user to press the new key combination.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyHint))]
    private bool _isListening;

    /// <summary>The last key press had no Ctrl, Alt or Win, so it was refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyHint))]
    private bool _rejectedNote;

    public string HotkeyHint => IsListening ? (RejectedNote ? "Add Ctrl or Alt" : "Press keys...") : "Double-click to set";

    partial void OnIsListeningChanged(bool value) => RejectedNote = false;

    public bool ShowEmptyNote => HasDevices && Chips.Count <= 1;

    /// <summary>One-line summary of the scope: "All displays", a device name, or "2 of 3 displays".</summary>
    public string ScopeSummary
    {
        get
        {
            var picked = Chips.Where(c => !c.IsAll && c.IsOn).ToList();
            if (!UseSelected || picked.Count == 0) return _allText;
            return picked.Count == 1 ? picked[0].Name : $"{picked.Count} of {Chips.Count - 1} {_noun}";
        }
    }

    public event Action<HotkeyAction>? Changed;

    /// <param name="deviceNoun">Plural noun for the scope chips, e.g. "displays". Null when the action has no devices.</param>
    /// <param name="group">Part of the feature the action belongs to ("Hardware", "Software"); null when the feature has no parts.</param>
    /// <param name="subgroup">Part of the group ("Presets"); null when the group is short enough not to need one.</param>
    public HotkeyAction(string id, string title, string category, string keywords,
        Action<IReadOnlyList<string>?> run, string? deviceNoun = null,
        Func<IReadOnlyList<HotkeyDevice>>? devices = null, Action<IReadOnlyList<string>?, int>? stepBy = null,
        string? group = null, string? subgroup = null)
    {
        Id = id;
        Title = title;
        Category = category;
        Group = group;
        Subgroup = subgroup;
        Panel = Services.Heading.Join(category, group);
        Heading = Services.Heading.Join(category, group, subgroup);
        Keywords = keywords;
        _run = run;
        _stepBy = stepBy;
        _deviceSource = devices;
        _noun = deviceNoun ?? "devices";
        _allText = $"All {_noun}";
        if (devices is not null) RefreshDevices();
    }

    /// <summary>
    /// A level action: moves <paramref name="level"/> by <see cref="DefaultStep"/> in <paramref name="direction"/> (+1 raise, -1 lower) when run,
    /// or by any amount a gesture asks for. <paramref name="step"/> gets the device ids (null = all) and the signed change in percent.
    /// Declaring the level here is all it takes for the gestures to offer it.
    /// </summary>
    public static HotkeyAction ForLevel(string id, string title, string category, string keywords, HotkeyLevel level, int direction,
        Action<IReadOnlyList<string>?, int> step, string? deviceNoun = null, Func<IReadOnlyList<HotkeyDevice>>? devices = null,
        string? group = null, string? subgroup = null) =>
        new(id, title, category, keywords, ids => step(ids, direction * DefaultStep), deviceNoun, devices,
            (ids, percent) => step(ids, direction * percent), group, subgroup)
        {
            Level = level,
            LevelDirection = direction,
        };

    /// <summary>Runs the action on every device, or only on the picked ones.</summary>
    public void Run() => _run(UseSelected ? _selectedIds.ToList() : null);

    /// <summary>Moves the level by <paramref name="percent"/> in the action's own direction (a level action only; others just run).</summary>
    public void RunBy(int percent)
    {
        if (_stepBy is null) Run();
        else _stepBy(UseSelected ? _selectedIds.ToList() : null, percent);
    }

    /// <summary>Re-reads the available devices, keeping the user's picks.</summary>
    public void RefreshDevices()
    {
        if (_deviceSource is null) return;
        _syncing = true;
        Chips.Clear();
        Chips.Add(new ScopeChip(null, _allText, !UseSelected, OnChipToggled));
        foreach (var d in _deviceSource())
            Chips.Add(new ScopeChip(d.Id, d.Name, UseSelected && _selectedIds.Contains(d.Id), OnChipToggled));
        _syncing = false;
        OnPropertyChanged(nameof(ShowEmptyNote));
        OnPropertyChanged(nameof(ScopeSummary));
    }

    public StoredHotkey Export() => new(Gesture, UseSelected, _selectedIds.ToList());

    public void Restore(StoredHotkey s)
    {
        _loading = true;
        Gesture = s.Gesture;
        _selectedIds.Clear();
        foreach (var id in s.DeviceIds) _selectedIds.Add(id);
        UseSelected = s.UseSelected && HasDevices;
        _loading = false;
        RefreshDevices();
    }

    /// <summary>Picks exactly this option ("All displays" or one device) and clears the rest.</summary>
    public void SelectOnly(ScopeChip chip)
    {
        _syncing = true;
        _selectedIds.Clear();
        if (!chip.IsAll) _selectedIds.Add(chip.DeviceId!);
        UseSelected = !chip.IsAll;
        foreach (var c in Chips)
            c.IsOn = c.IsAll ? !UseSelected : UseSelected && _selectedIds.Contains(c.DeviceId!);
        _syncing = false;
        OnPropertyChanged(nameof(ScopeSummary));
        Raise();
    }

    /// <summary>"All" and device chips behave like one choice: All, or a set of devices.</summary>
    private void OnChipToggled(ScopeChip chip)
    {
        if (_syncing) return;
        _syncing = true;

        if (chip.IsAll)
        {
            _selectedIds.Clear();
            UseSelected = false;
            chip.IsOn = true; // All cannot be switched off on its own
        }
        else
        {
            if (chip.IsOn) _selectedIds.Add(chip.DeviceId!); else _selectedIds.Remove(chip.DeviceId!);
            UseSelected = _selectedIds.Count > 0;
        }

        foreach (var c in Chips)
            c.IsOn = c.IsAll ? !UseSelected : UseSelected && _selectedIds.Contains(c.DeviceId!);

        _syncing = false;
        OnPropertyChanged(nameof(ScopeSummary));
        Raise();
    }

    partial void OnGestureChanged(string value) => Raise();

    private void Raise()
    {
        if (!_loading) Changed?.Invoke(this);
    }
}
