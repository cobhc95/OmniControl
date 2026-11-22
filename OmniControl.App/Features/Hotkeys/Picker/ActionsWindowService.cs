using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Ui.Restore;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The Actions and Hotkeys window: the Choose-the-action window in its manage mode over every master
/// action, opened from the sidebar's bottom entry and from search results; one non-modal window, brought to the front when asked for again,
/// hidden rather than closed (<see cref="SingleWindow{T}"/> keep), so it opens again at once with a fresh model, until it is let go after a long hide.
/// It owns what the window's top bar shows: the shortcut sets (a preset group of the master list, with the shared preset picker), Clear all
/// and Restore defaults. Everything it lists is read live from <see cref="HotkeyRegistry"/>.
public sealed class ActionsWindowService : IDisposable
{
    public const string Title = PageTitles.ActionsAndHotkeys;

    /// The sidebar entry's icon (the list with a magnifier of the former All actions page).
    public const string Icon = "M3,5 H14 V7 H3 Z M3,10 H10 V12 H3 Z M3,15 H8 V17 H3 Z M16,9 A4.5,4.5 0 1 1 16,18 A4.5,4.5 0 1 1 16,9 Z M16,11 A2.5,2.5 0 1 0 16,16 A2.5,2.5 0 1 0 16,11 Z M19,17.5 L21.5,20 L20,21.5 L17.5,19 Z";

    // Kept from the Shortcuts page, so the preset manager's set group, the restore snapshot and the restore action keep their ids.
    private const string SetsGroupId = "shortcuts.sets", RestoreKey = "shortcuts";

    private readonly HotkeyRegistry _registry;
    private readonly HotkeySets _sets;
    private readonly ProviderChoice _setChoice;
    private readonly Action<string?, string?> _openPresets;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _showPage;
    private readonly Func<Avalonia.Visual, HotkeyAction, Task> _changeIcon;
    private readonly SingleWindow<PickerWindow> _window;
    private readonly FavouriteActions _favourites;
    private PickerModel? _model;

    /// <param name="presets">The master list of preset groups: the shortcut sets register in it, so the preset manager lists them.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group ("Manage presets...").</param>
    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    /// <param name="showPage">Opens the sidebar page with this title ("Show on its page").</param>
    /// <param name="changeIcon">The shared icon picker for an action ("Change icon…" in its settings).</param>
    /// <param name="hotkeysSwitch">The master switch the top bar shows at its left.</param>
    /// <param name="report">Says on the footer when the window cannot be opened.</param>
    public ActionsWindowService(HotkeyRegistry registry, IPresetGroups presets, Action<string?, string?> openPresets,
        Func<DialogMessage, Task<bool>> ask, Action<string> showPage, Func<Avalonia.Visual, HotkeyAction, Task> changeIcon, HotkeysSwitch hotkeysSwitch, Action<string> report)
    {
        (_registry, _openPresets, _ask, _showPage, _changeIcon) = (registry, openPresets, ask, showPage, changeIcon);
        HotkeysSwitch = hotkeysSwitch;
        _window = new SingleWindow<PickerWindow>(report, keep: true);
        _sets = new HotkeySets(registry);
        _favourites = new FavouriteActions(registry);
        var setPresets = new HotkeySetPresets(_sets);
        _setChoice = new ProviderChoice("Shortcut sets", setPresets, () => _openPresets(Title, SetsGroupId));
        presets.Register(new PresetGroup(SetsGroupId, Title, "Shortcut sets", setPresets));
        Restorable = new SettingsRestore<Dictionary<string, StoredHotkey>>(RestoreKey, Title,
            () => "Puts every action back on its default keyboard shortcut (most have none) and on all its devices.",
            registry.ExportAll, registry.DefaultBindings,
            all => { registry.ImportAll(all); return Task.FromResult(true); },
            atDefaults: all => HotkeyDefaults.IsAtDefaults(all, DefaultKeyFrom(registry.DefaultBindings())));
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        registry.BindingsSaved += OnBindingsSaved;
        PickerWindow.UseKeys(Keys);
        DrawFirst.ThenWire(PickerWindow.Warm);
    }

    private static Func<string, string?> DefaultKeyFrom(IReadOnlyDictionary<string, StoredHotkey> defaults) =>
        id => defaults.TryGetValue(id, out var saved) ? saved.Keys.FirstOrDefault() : null;

    /// What "Restore defaults" puts back (every key removed, every action on all its devices); the shell wires it like a page's.
    public IRestorable Restorable { get; }

    /// The window's "Restore defaults", given by the shell from <see cref="Restorable"/>.
    public PageRestore? Restorer { get; set; }

    /// The hotkeys master switch the top bar shows.
    public HotkeysSwitch HotkeysSwitch { get; }

    public IAsyncRelayCommand ClearAllCommand { get; }

    /// The shortcut-set drop-down of the window's top bar (the shared preset picker).
    public ChoiceItem SetChoice => _setChoice.Choice;

    /// One search result per action: its name, and "Open" for the window at that action (the global search's way to a key).
    public IEnumerable<SearchEntry> SearchEntries => _registry.Actions.Select(a => new SearchEntry
    {
        Title = a.Title + " hotkey",
        Category = a.Category,
        Group = a.PickerHeading,
        RemotePc = a.RemotePc,
        Keywords = $"{a.Keywords} keyboard shortcut keybinding {string.Join(' ', a.Keys)} {a.OptionWords}",
        Editor = new ButtonItem(a.Title, "Open", Title, () => Open(a.Id)),
    });

    /// Opens the window (or brings it to the front), at the action with this id when one is given.
    public void Open(string? actionId = null)
    {
        _window.Show(() => PickerWindow.Manage(Title, this), Left, $"Couldn't open {Title}", Opening);
        if (actionId is not null) _model?.ShowEntry($"{MouseActionCatalog.Omni}:{actionId}");
    }

    /// Each showing starts fresh, as a newly made window did: a new model over the live master list (search empty, first section).
    /// Lets the hidden window go (OmniControl has sat in the tray a long while); the next opening makes it again.
    public void Release() => _window.Release();

    private void Opening(PickerWindow window) => window.Use(_model = new PickerModel(
        PickChoice.Group(MouseActionCatalog.ManageChoices(_registry.PickerActions)), null, new Dictionary<string, string>(), manage: Keys));

    private void Left(PickerWindow window)
    {
        window.Leave();
        _model = null;
    }

    /// The keys part every Choose-the-action window shows, read from the live master list (pick mode gets it from the window).
    public ManageMode Keys => new(KeysOf, PaneFor, ActionOf, _registry, _favourites, _ask);

    private HotkeyAction? ActionOf(PickChoice choice) => MouseActionCatalog.OmniIdOf(choice.Spec) is { } id ? _registry.Find(id) : null;

    private IReadOnlyList<string>? KeysOf(PickChoice choice) => ActionOf(choice)?.Keys;

    private ActionHotkeys? PaneFor(PickChoice choice) => ActionOf(choice) is { } action ? new ActionHotkeys(action, _registry, Show, _changeIcon) : null;

    /// "Show on its page", or for a preset group's action "Show in Presets Manager" at its group.
    private void Show(HotkeyAction action)
    {
        if (action.Preset is { } preset) _openPresets(preset.Section, preset.GroupId);
        else _showPage(action.Category);
    }

    private void OnBindingsSaved() => Restorer?.Refresh();

    /// Asks once, then takes every key off every action (the device choices stay); the set in use keeps the result.
    private async Task ClearAllAsync()
    {
        if (!await _ask(new DialogMessage("Clear all shortcuts?", $"Every shortcut in \"{_sets.Current.Name}\" is removed. Other sets are not changed.", "Clear all"))) return;
        _registry.ImportAll(_registry.ExportAll().ToDictionary(p => p.Key, p => p.Value with { Gesture = string.Empty, More = null }));
    }

    /// Takes the shortcut sets from disk as they are now (an import wrote them); false when the file holds none.
    public bool ReloadShortcutSets() => _sets.ReloadFromDisk();

    /// Takes the favourite actions from disk as they are now (an import wrote them).
    public bool ReloadFavourites() => _favourites.ReloadFromDisk();

    public void Dispose()
    {
        _registry.BindingsSaved -= OnBindingsSaved;
        _window.Close();
        _setChoice.Dispose();
        _sets.Dispose();
        _favourites.Dispose();
    }
}
