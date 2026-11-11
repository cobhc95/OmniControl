using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Hotkeys;

/// <summary>What gets saved for one hotkey.</summary>
public sealed record StoredHotkey(string Gesture, bool UseSelected, List<string> DeviceIds);

/// <summary>
/// A bindable hotkey. Every feature registers its actions through <see cref="HotkeyRegistry"/>.
/// Pass a device source when the action targets variable devices (monitors...): the user can then
/// choose all devices or a hand-picked subset.
/// </summary>
public sealed partial class HotkeyAction : ObservableObject, Pages.IEditorItem
{
    string Pages.IEditorItem.Label => Title;

    private readonly Action<IReadOnlyList<string>?> _run;
    private readonly Func<IReadOnlyList<HotkeyDevice>>? _deviceSource;
    private readonly string _allText;
    private readonly string _noun;
    private readonly HashSet<string> _selectedIds = new();
    private bool _loading;
    private bool _syncing;

    public string Id { get; }
    public string Title { get; }
    public string Category { get; }
    public string Keywords { get; }

    public bool HasDevices => _deviceSource is not null;

    /// <summary>"All displays" first, then one chip per device.</summary>
    public ObservableCollection<ScopeChip> Chips { get; } = new();

    /// <summary>Like "Ctrl+Alt+Up". Empty means not bound.</summary>
    [ObservableProperty] private string _gesture = string.Empty;

    [ObservableProperty] private bool _useSelected;

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

    /// <summary>Set by the registry: true for the first action of each feature, so the page can show a heading.</summary>
    [ObservableProperty] private bool _showGroupHeader;

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
    public HotkeyAction(string id, string title, string category, string keywords,
        Action<IReadOnlyList<string>?> run, string? deviceNoun = null,
        Func<IReadOnlyList<HotkeyDevice>>? devices = null)
    {
        Id = id;
        Title = title;
        Category = category;
        Keywords = keywords;
        _run = run;
        _deviceSource = devices;
        _noun = deviceNoun ?? "devices";
        _allText = $"All {_noun}";
        if (devices is not null) RefreshDevices();
    }

    /// <summary>Runs the action on every device, or only on the picked ones.</summary>
    public void Run() => _run(UseSelected ? _selectedIds.ToList() : null);

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
