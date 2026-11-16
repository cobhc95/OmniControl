using System.Collections.ObjectModel;
using OmniControl.App.Dialogs;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Presets;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>Every registered hotkey as an editable, searchable card. Features add theirs via <see cref="HotkeyRegistry"/>.</summary>
public sealed partial class ShortcutsPageViewModel : PageViewModel, IDisposable
{
    private const string SetsGroupId = "shortcuts.sets";

    private readonly HotkeyRegistry _registry;
    private readonly HotkeySets _sets;
    private readonly ProviderChoice _picker;
    private readonly Action<string?, string?> _openPresets;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    public override string Title => "Shortcuts";

    public override string? NavGroup => NavGroups.Workflow;

    public override string KeyHelp =>
        "Left/Right choose the hotkey or the devices. Enter (or a click) on the hotkey listens for keys: Esc cancels, Delete clears. "
        + "Enter on the devices opens the list: Up/Down move, Space ticks several, Enter picks and closes.";
    public override string Icon => "M4,5 H20 A2,2 0 0 1 22,7 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V7 A2,2 0 0 1 4,5 Z M4,7 V17 H20 V7 Z M5,9 H7 V11 H5 Z M9,9 H11 V11 H9 Z M13,9 H15 V11 H13 Z M17,9 H19 V11 H17 Z M7,14 H17 V16 H7 Z";
    public override string Keywords => "keyboard keys hotkeys hotkey keybinding bind shortcut";

    private ObservableCollection<HotkeyAction> Actions => _registry.Actions;

    public override bool ShowsOwnHeader => true; // the shortcut sets and "Clear all" sit on the title line

    /// <summary>The drop-down of the shortcut sets (the shared preset picker).</summary>
    public ChoiceItem SetChoice => _picker.Choice;

    /// <summary>The actions grouped by page of the sidebar, in the sidebar's order; a page's groups and their subgroups fold inside.</summary>
    public ObservableCollection<HotkeyGroup> Groups { get; } = new();

    public override IEnumerable<SearchEntry> SearchEntries =>
        Actions.Select(a => Entry(a, a.Title + " hotkey", $"{a.Keywords} keyboard shortcut keybinding", a.Heading));

    /// <param name="presets">The master list of preset groups: the shortcut sets register in it, so the preset manager lists them.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group ("Manage presets...").</param>
    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    public ShortcutsPageViewModel(HotkeyRegistry registry, PresetGroups presets, Action<string?, string?> openPresets, Func<DialogMessage, Task<bool>> ask)
    {
        (_registry, _openPresets, _ask) = (registry, openPresets, ask);
        _sets = new HotkeySets(registry);
        var setPresets = new HotkeySetPresets(_sets);
        _picker = new ProviderChoice("Shortcut sets", setPresets, ManageSets);
        presets.Register(new PresetGroup(SetsGroupId, Title, "Shortcut sets", setPresets));
        Rebuild();
        registry.Actions.CollectionChanged += OnActionsChanged; // features register at different times
        registry.BindingsSaved += RefreshCounts;
    }

    private void OnActionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Groups.Clear();
        foreach (var g in Actions.GroupBy(a => a.Category).OrderBy(g => _registry.RankOf(g.Key)))
            Groups.Add(new HotkeyGroup(g.Key, g.ToList(), UseSuggested));
    }

    private void RefreshCounts()
    {
        foreach (var group in Groups) group.RefreshCounts();
    }

    /// <summary>Opens the preset manager at the shortcut sets.</summary>
    public void ManageSets() => _openPresets(Title, SetsGroupId);

    /// <summary>Asks once, then takes every shortcut off every action (the device choices stay); the set in use keeps the result.</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (!await _ask(new DialogMessage("Clear all shortcuts?", $"Every shortcut in \"{_sets.Current.Name}\" is removed. Other sets are not changed.", "Clear all"))) return;
        _registry.ImportAll(_registry.ExportAll().ToDictionary(p => p.Key, p => p.Value with { Gesture = string.Empty }));
    }

    /// <summary>Binds the suggested keys of one panel's actions and says in one sentence which were left out because their key is taken.</summary>
    private void UseSuggested(IReadOnlyList<HotkeyAction> actions)
    {
        var skipped = _registry.UseSuggested(actions);
        if (skipped.Count > 0)
            _registry.Report?.Invoke($"Left out, because another action already uses the key: {string.Join(", ", skipped)}.");
    }

    public void Dispose()
    {
        _registry.Actions.CollectionChanged -= OnActionsChanged;
        _registry.BindingsSaved -= RefreshCounts;
        _picker.Dispose();
        _sets.Dispose();
    }

    public override void OnShown()
    {
        foreach (var a in Actions) a.RefreshDevices();
    }
}
