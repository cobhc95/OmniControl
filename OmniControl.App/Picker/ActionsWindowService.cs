using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Dialogs;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Pages;
using OmniControl.App.Pages.Restore;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>
/// The Actions and hotkeys window (docs/mockups/actions-and-hotkeys.html): the Choose-the-action window in its manage mode over every master
/// action, opened from the sidebar's bottom entry and from search results; one non-modal window, brought to the front when asked for again.
/// It owns what the window's top bar shows: the shortcut sets (a preset group of the master list, with the shared preset picker), Clear all
/// and Restore defaults. Everything it lists is read live from <see cref="HotkeyRegistry"/>.
/// </summary>
public sealed class ActionsWindowService : IDisposable
{
    public const string Title = "Actions and hotkeys";

    /// <summary>The sidebar entry's icon (the list with a magnifier of the former All actions page).</summary>
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
    private PickerModel? _model;

    /// <param name="presets">The master list of preset groups: the shortcut sets register in it, so the preset manager lists them.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group ("Manage presets...").</param>
    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    /// <param name="showPage">Opens the sidebar page with this title ("Show on its page").</param>
    /// <param name="changeIcon">The shared icon picker for an action ("Change icon…" in its settings).</param>
    /// <param name="hotkeysSwitch">The master switch the top bar shows at its left.</param>
    /// <param name="report">Says on the footer when the window cannot be opened.</param>
    public ActionsWindowService(HotkeyRegistry registry, PresetGroups presets, Action<string?, string?> openPresets,
        Func<DialogMessage, Task<bool>> ask, Action<string> showPage, Func<Avalonia.Visual, HotkeyAction, Task> changeIcon, HotkeysSwitch hotkeysSwitch, Action<string> report)
    {
        (_registry, _openPresets, _ask, _showPage, _changeIcon) = (registry, openPresets, ask, showPage, changeIcon);
        HotkeysSwitch = hotkeysSwitch;
        _window = new SingleWindow<PickerWindow>(report);
        _sets = new HotkeySets(registry);
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
        if (!StartupMode.Inert) DrawFirst.ThenWire(PickerWindow.Warm);
    }

    private static Func<string, string?> DefaultKeyFrom(IReadOnlyDictionary<string, StoredHotkey> defaults) =>
        id => defaults.TryGetValue(id, out var saved) ? saved.Keys.FirstOrDefault() : null;

    /// <summary>What "Restore defaults" puts back (every key removed, every action on all its devices); the shell wires it like a page's.</summary>
    public IRestorable Restorable { get; }

    /// <summary>The window's "Restore defaults", given by the shell from <see cref="Restorable"/>.</summary>
    public PageRestore? Restorer { get; set; }

    /// <summary>The hotkeys master switch the top bar shows.</summary>
    public HotkeysSwitch HotkeysSwitch { get; }

    public IAsyncRelayCommand ClearAllCommand { get; }

    /// <summary>The shortcut-set drop-down of the window's top bar (the shared preset picker).</summary>
    public ChoiceItem SetChoice => _setChoice.Choice;

    /// <summary>One search result per action: its name, and "Open" for the window at that action (the global search's way to a key).</summary>
    public IEnumerable<SearchEntry> SearchEntries => _registry.Actions.Select(a => new SearchEntry
    {
        Title = a.Title + " hotkey",
        Category = a.Category,
        Group = a.PickerHeading,
        RemotePc = a.RemotePc,
        Keywords = $"{a.Keywords} keyboard shortcut keybinding {string.Join(' ', a.Keys)}",
        Editor = new ButtonItem(a.Title, "Open", Title, () => Open(a.Id)),
    });

    /// <summary>Opens the window (or brings it to the front), at the action with this id when one is given.</summary>
    public void Open(string? actionId = null)
    {
        if (_window.Current is null)
            _window.Show(() =>
            {
                _model = new PickerModel(PickChoice.Group(MouseActionCatalog.ManageChoices(_registry.PickerActions)), null, new Dictionary<string, string>(),
                    manage: Keys);
                return PickerWindow.Manage(Title, _model, this);
            }, _ => Closed(), $"Couldn't open {Title}");
        else _window.Current.Activate();
        if (actionId is not null) _model?.ShowEntry($"{MouseActionCatalog.Omni}:{actionId}");
    }

    private void Closed() => _model = null;

    /// <summary>The keys part every Choose-the-action window shows, read from the live master list (pick mode gets it from the window).</summary>
    public ManageMode Keys => new(KeysOf, PaneFor, ActionOf, _registry);

    private HotkeyAction? ActionOf(PickChoice choice) => MouseActionCatalog.OmniIdOf(choice.Spec) is { } id ? _registry.Find(id) : null;

    private IReadOnlyList<string>? KeysOf(PickChoice choice) => ActionOf(choice)?.Keys;

    private ActionHotkeys? PaneFor(PickChoice choice) => ActionOf(choice) is { } action ? new ActionHotkeys(action, _registry, _showPage, _changeIcon) : null;

    private void OnBindingsSaved() => Restorer?.Refresh();

    /// <summary>Asks once, then takes every key off every action (the device choices stay); the set in use keeps the result.</summary>
    private async Task ClearAllAsync()
    {
        if (!await _ask(new DialogMessage("Clear all shortcuts?", $"Every shortcut in \"{_sets.Current.Name}\" is removed. Other sets are not changed.", "Clear all"))) return;
        _registry.ImportAll(_registry.ExportAll().ToDictionary(p => p.Key, p => p.Value with { Gesture = string.Empty, More = null }));
    }

    public void Dispose()
    {
        _registry.BindingsSaved -= OnBindingsSaved;
        _window.Close();
        _setChoice.Dispose();
        _sets.Dispose();
    }
}
