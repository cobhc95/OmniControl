using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Hotkeys;

/// <summary>What gets saved for one hotkey: its first key, the device choice, its further keys (<see cref="More"/>; absent in files written
/// before an action could have several keys, which is how those read as one key) and the action's own settings that differ from their
/// defaults (<see cref="Options"/>; absent until one is changed), which every hotkey and rule that runs the action uses.</summary>
public sealed record StoredHotkey(string Gesture, bool UseSelected, List<string> DeviceIds, List<string>? More = null, Dictionary<string, string>? Options = null)
{
    /// <summary>Every key, the first one first.</summary>
    public IEnumerable<string> Keys => (More ?? new List<string>()).Prepend(Gesture).Where(k => k.Length > 0);
}

/// <summary>A name the user may change in the pickers: what it is now, and what to do with the new one (returns the action's new title, or null when nothing changed).</summary>
public sealed record Renaming(string Current, Func<string, string?> Apply);

/// <summary>
/// A level that a pair of actions moves up and down (volume, brightness...). <see cref="Id"/> is stored by the gestures that drive it
/// (gestures.json), so it never changes; <see cref="Name"/> is what the gesture picker calls it.
/// </summary>
public sealed record HotkeyLevel(string Id, string Name);

/// <summary>
/// Actions that are the directions of one thing ("Pointer speed": Up, Down) and are listed as ONE row in the Actions and hotkeys window and the
/// Choose-the-action window. Every direction stays its own <see cref="HotkeyAction"/> with its own id, keys and settings; only the lists group them.
/// </summary>
public sealed record ActionFamily(string Id, string Title);

/// <summary>
/// A bindable hotkey, and the master entry for everything OmniControl itself can do. Every feature registers its actions through
/// <see cref="HotkeyRegistry"/>; the Actions and hotkeys window, search, the mouse button and gesture pickers all read them from there.
/// Pass a device source when the action targets variable devices (monitors...): the user can then
/// choose all devices or a hand-picked subset.
/// </summary>
public sealed partial class HotkeyAction : HotkeyTarget, Pages.IEditorItem
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

    /// <summary>A shorter name for tight places (the tray flyout's tiles), set on the master entry; the <see cref="Title"/> when not given.</summary>
    public string? Short { get; init; }

    public string ShortName => Short ?? Title;

    /// <summary>The action's own glyph, the key of a <c>StreamGeometry</c> in <c>Styles/Brushes.axaml</c> (<see cref="Services.IconResources"/>); null: the pickers and the tray show its page's icon.</summary>
    public string? Icon { get; init; }

    /// <summary>Two or three library icon ids (<see cref="Icons.IconLibrary"/>) that suit the action: the icon picker's "Suggested for this", and the first is its default icon.</summary>
    public IReadOnlyList<string>? SuggestedIcons { get; init; }

    /// <summary>The icon the user chose for it (a library id or a user icon), set by <see cref="Icons.IconStore"/> as it is registered; null: its default.</summary>
    public string? ChosenIcon { get; set; }

    /// <summary>The icon it shows: the chosen one, else its first suggestion, else its own <see cref="Icon"/>; null: its page's icon.</summary>
    public string? ShownIcon => ChosenIcon ?? Icons.IconLibrary.DefaultOf(SuggestedIcons, Icon);

    /// <summary>The feature the action belongs to: the title of the page that registers it.</summary>
    public string Category { get; }

    /// <summary>Optional part of the feature ("Hardware", "Software").</summary>
    public string? Group { get; }

    /// <summary>Optional part of the group ("Colour (RGB and gamma)"): the pickers show it in the group heading.</summary>
    public string? Subgroup { get; }

    /// <summary>Category, group and subgroup as one line ("Displays - Hardware - Presets"): what every list shows as the action's category.</summary>
    public string Heading { get; }

    public string Keywords { get; }

    /// <summary>
    /// A key combination the feature suggests for this action ("Ctrl+Alt+Up"), or null. It is never bound by itself: the Actions and hotkeys window's
    /// "Suggest" binds it on request.
    /// </summary>
    public string? SuggestedKey { get; init; }

    /// <summary>
    /// The key combination the action starts with ("Alt+Z"), or null. <see cref="HotkeyRegistry"/> binds it when the action is first registered
    /// (never again once the user has given or cleared its keys, and not when another action already has it); "Restore defaults" puts it back.
    /// </summary>
    public string? DefaultKey { get; init; }

    /// <summary>
    /// True for the actions that keep the user in control (lock, open OmniControl, pause all rules): no automation block rule may stop
    /// them. A flag on the master entry, so there is no second list of ids.
    /// </summary>
    public bool NeverBlock { get; init; }

    /// <summary>The level this action moves, for a level action made with <see cref="ForLevel"/>; null for any other action.</summary>
    public HotkeyLevel? Level { get; private init; }

    /// <summary>+1 when this level action raises its level, -1 when it lowers it.</summary>
    public int LevelDirection { get; private init; }

    private readonly ActionFamily? _family;
    private readonly string? _familyRole;

    /// <summary>The family this action is one direction of (see <see cref="ActionFamily"/>); a level action is the family of its level, so it needs none set. Null: listed alone.</summary>
    public ActionFamily? Family
    {
        get => _family ?? (Level is { } level ? new ActionFamily(level.Id, level.Name) : null);
        init => _family = value;
    }

    /// <summary>The word for this action's direction inside its family ("Up", "Down", "Next", "On"); a level action says Up or Down by itself.</summary>
    public string? FamilyRole
    {
        get => _familyRole ?? (Level is null ? null : LevelDirection > 0 ? "Up" : "Down");
        init => _familyRole = value;
    }

    /// <summary>The part of the Choose-the-action window this action is listed under, when that is not the page that registers it (Power, Launch).</summary>
    public string? PickerSection { get; init; }

    /// <summary>The section the pickers list the action under: <see cref="PickerSection"/>, or its <see cref="Category"/>.</summary>
    public string Section => PickerSection ?? Category;

    /// <summary>Where the action's section stands in the pickers' section list and its icon; set by <see cref="HotkeyRegistry"/> from the live page list.</summary>
    public ActionSection? SectionPlace { get; set; }

    /// <summary>
    /// The group heading inside the section ("Hardware - Presets"): group and subgroup as one line; an action listed under another section than its
    /// own category (the apps, under Launch) takes the category as its heading; null when there is none or it only repeats the section.
    /// </summary>
    public string? PickerHeading =>
        (Services.Heading.Join(Group, Subgroup) is { Length: > 0 } heading ? heading : PickerSection is not null && PickerSection != Category ? Category : null) is { } line && line != Section ? line : null;

    /// <summary>The settings of its own the action takes from an input that runs it (a program to start, a countdown...); a level action always also takes its step (<see cref="Takes"/>).</summary>
    public ActionSettings Accepts { get; init; }

    /// <summary>What the pickers offer settings for.</summary>
    public ActionSettings Takes => Level is null ? Accepts : Accepts | ActionSettings.Step;

    /// <summary>How to run the action with the settings an input chose; null: it takes none beyond a level action's step.</summary>
    public Action<RunSettings>? RunConfigured { get; init; }

    /// <summary>For an action that can last while its input is held (repeated clicking): starts it with the settings the input chose and returns what ends it, or null when the settings say it does not last. Called on the input's thread, so it must return at once.</summary>
    public Func<RunSettings, Action?>? RunHeld { get; init; }

    /// <summary>Set: the pickers show "Remove" (or the hide wording) in this action's settings; it takes the action out of the master list (see <see cref="HotkeyRegistry.PickerActions"/>).</summary>
    public Action? Remove { get; init; }

    /// <summary>The words of the remove button ("Remove program", "Hide app").</summary>
    public string RemoveText { get; init; } = "Remove";

    /// <summary>Set: the user may rename what the action is about in the pickers (a program they added).</summary>
    public Renaming? Renaming { get; init; }

    /// <summary>Set: the pickers show these settings for the action and store each change at once (a custom action), or, with <see cref="ActionEditor.Create"/>, the form that makes one; see <see cref="ActionEditor"/>.</summary>
    public ActionEditor? Editor { get; init; }

    /// <summary>The Mouse and Trackpad picker lists its own version of this action (the layer actions, with their wrap and hold settings), so it leaves this one out.</summary>
    public bool CoveredByMousePicker { get; init; }

    /// <summary>Set: this is a button of its section's list in the pickers (add a program), not an action; it returns the actions it made, already registered.</summary>
    public Func<Avalonia.Visual, Task<IReadOnlyList<HotkeyAction>>>? Command { get; init; }

    /// <summary>For a <see cref="Command"/>: false hides the button for now ("Show hidden apps" while nothing is hidden).</summary>
    public Func<bool>? IsListed { get; init; }

    /// <summary>A small fact shown as a tag beside the entry's name in the Actions and hotkeys window ("active"); null: none. Set again by the owner when the fact changes.</summary>
    public string? Tag { get; init; }

    /// <summary>Set: the editor (a heading and a panel row) the Actions and hotkeys window shows in this action's settings, besides its defaults (the power plan editor).</summary>
    public IReadOnlyList<ActionOption>? ManageSettings { get; init; }

    /// <summary>Called each time a picker listing this entry opens (the apps are looked for again).</summary>
    public Action? PickerOpened { get; init; }

    public bool HasDevices => _deviceSource is not null;

    /// <summary>"All displays" first, then one chip per device.</summary>
    public ObservableCollection<ScopeChip> Chips { get; } = new();

    /// <summary>The action's further keys after <see cref="HotkeyTarget.Gesture"/> (its first); an action may have several, each unique in the set.</summary>
    [ObservableProperty] private IReadOnlyList<string> _moreKeys = Array.Empty<string>();

    /// <summary>Every key of the action, the first one first; empty when it has none.</summary>
    public IReadOnlyList<string> Keys => Gesture.Length == 0 ? MoreKeys : MoreKeys.Prepend(Gesture).ToList();

    /// <summary>Gives the action exactly these keys (empty ones dropped, no key twice), with one save.</summary>
    public void SetKeys(IEnumerable<string> keys)
    {
        var all = keys.Where(k => k.Length > 0).Distinct().ToList();
        _loading = true;
        Gesture = all.FirstOrDefault() ?? string.Empty;
        MoreKeys = all.Skip(1).ToList();
        _loading = false;
        Raise();
    }

    [ObservableProperty] private bool _useSelected;

    /// <summary>
    /// The action's own settings that differ from their defaults (a program to start, how far a level moves, a countdown): what its hotkeys and
    /// rules run it with, and what a mouse button, gesture or rule step uses while it has no copy of its own. Kept with the keys, so a shortcut set holds them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defaults { get; private set; } = new Dictionary<string, string>();

    /// <summary>Takes these as the action's settings (only those that differ from the option's default are kept), with one save.</summary>
    public void SetDefaults(IReadOnlyDictionary<string, string> values)
    {
        Defaults = new Dictionary<string, string>(values);
        OnPropertyChanged(nameof(Defaults));
        Raise();
    }

    /// <summary>
    /// Why the action does nothing right now (a device it needs is missing), set and cleared live by the feature that owns it;
    /// null = it works. The Actions and hotkeys window shows it on the action; the key can still be bound.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUnavailable))] private string? _unavailableReason;

    public bool IsUnavailable => UnavailableReason is not null;

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
        string? group = null, string? subgroup = null, string? icon = null) =>
        new(id, title, category, keywords, ids => step(ids, direction * DefaultStep), deviceNoun, devices,
            (ids, percent) => step(ids, direction * percent), group, subgroup)
        {
            Icon = icon,
            Level = level,
            LevelDirection = direction,
        };

    /// <summary>Runs the action on every device, or only on the picked ones.</summary>
    public void Run() => _run(UseSelected ? _selectedIds.ToList() : null);

    /// <summary>Runs the action with the settings the input that fired it chose (others just run).</summary>
    public void RunWith(RunSettings settings)
    {
        if (RunConfigured is not null) RunConfigured(settings);
        else if (settings.Step is { } percent && Level is not null) RunBy(percent);
        else Run();
    }

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

    public StoredHotkey Export() => new(Gesture, UseSelected, _selectedIds.ToList(), MoreKeys.Count > 0 ? MoreKeys.ToList() : null,
        Defaults.Count > 0 ? new Dictionary<string, string>(Defaults) : null);

    public void Restore(StoredHotkey s)
    {
        _loading = true;
        var keys = s.Keys.Distinct().ToList();
        Gesture = keys.FirstOrDefault() ?? string.Empty;
        MoreKeys = keys.Skip(1).ToList();
        _selectedIds.Clear();
        foreach (var id in s.DeviceIds) _selectedIds.Add(id);
        UseSelected = s.UseSelected && HasDevices;
        Defaults = s.Options is { Count: > 0 } ? new Dictionary<string, string>(s.Options) : new Dictionary<string, string>();
        OnPropertyChanged(nameof(Defaults));
        _loading = false;
        RefreshDevices();
    }

    /// <summary>Takes the device choice of another direction of the same family (its settings are shared), with one save when it differs.</summary>
    public void CopyScopeFrom(HotkeyAction other)
    {
        if (!HasDevices || (UseSelected == other.UseSelected && _selectedIds.SetEquals(other._selectedIds))) return;
        _selectedIds.Clear();
        foreach (var id in other._selectedIds) _selectedIds.Add(id);
        UseSelected = other.UseSelected;
        RefreshDevices();
        Raise();
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

    protected override void GestureSet()
    {
        OnPropertyChanged(nameof(Keys));
        Raise();
    }

    partial void OnMoreKeysChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(Keys));
        Raise();
    }

    private void Raise()
    {
        if (!_loading) Changed?.Invoke(this);
    }
}
