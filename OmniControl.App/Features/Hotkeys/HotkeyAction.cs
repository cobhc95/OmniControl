namespace OmniControl.App.Features.Hotkeys;

/// What gets saved for one hotkey: its first key, the device choice, its further keys (<see cref="More"/>; absent in files written
/// before an action could have several keys, which is how those read as one key) and the action's own settings that differ from their
/// defaults (<see cref="Options"/>; absent until one is changed), which every hotkey and rule that runs the action uses.
public sealed record StoredHotkey(string Gesture, bool UseSelected, List<string> DeviceIds, List<string>? More = null, Dictionary<string, string>? Options = null)
{
    /// Every key, the first one first.
    public IEnumerable<string> Keys => (More ?? new List<string>()).Prepend(Gesture).Where(k => k.Length > 0);
}

/// A name the user may change in the pickers: what it is now, and what to do with the new one (returns the action's new title, or null when nothing changed).
public sealed record Renaming(string Current, Func<string, string?> Apply);

/// The words of a power action's countdown: while it counts down ("Sleeping") and once it ran out ("Going to sleep…").
public sealed record CountdownWords(string Doing, string Going);

/// A level that a pair of actions moves up and down (volume, brightness...). <see cref="Id"/> is stored by the gestures that drive it
/// (gestures.json), so it never changes; <see cref="Name"/> is what the gesture picker calls it.
public sealed record HotkeyLevel(string Id, string Name);

/// Actions that are the directions of one thing ("Pointer speed": Up, Down) and are listed as ONE row in the Actions and Hotkeys window and the
/// Choose-the-action window. Every direction stays its own <see cref="HotkeyAction"/> with its own id, keys and settings; only the lists group them.
public sealed record ActionFamily(string Id, string Title);

/// A bindable hotkey, and the master entry for everything OmniControl itself can do. Every feature registers its actions through
/// <see cref="HotkeyRegistry"/>; the Actions and Hotkeys window, search, the mouse button and gesture pickers all read them from there.
/// Pass a device source when the action targets variable devices (monitors...): the user can then
/// choose all devices or a hand-picked subset.
public sealed partial class HotkeyAction : HotkeyTarget, Shared.Ui.Pages.IEditorItem
{
    string Shared.Ui.Pages.IEditorItem.Label => Title;

    private readonly Action<IReadOnlyList<string>?> _run;
    private readonly Action<IReadOnlyList<string>?, int>? _stepBy;
    private readonly Func<IReadOnlyList<HotkeyDevice>>? _deviceSource;
    private readonly string _allText;
    private readonly string _noun;
    private readonly HashSet<string> _selectedIds = new();
    private bool _loading;
    private bool _syncing;

    /// How much a level action (volume, brightness...) changes per press, in percent, when nothing else is asked for.
    public const int DefaultStep = 5;

    public string Id { get; }
    public string Title { get; }

    /// A shorter name for tight places (the tray flyout's tiles), set on the master entry; the <see cref="Title"/> when not given.
    public string? Short { get; init; }

    public string ShortName => Short ?? Title;

    /// The action's own glyph, the key of a <c>StreamGeometry</c> in <c>Styles/Brushes.axaml</c> (<see cref="Icons.IconResources"/>); null: the pickers and the tray show its page's icon.
    public string? Icon { get; init; }

    /// Two or three library icon ids (<see cref="Icons.IconLibrary"/>) that suit the action: the icon picker's "Suggested for this", and the first is its default icon.
    public IReadOnlyList<string>? SuggestedIcons { get; init; }

    /// The icon the user chose for it (a library id or a user icon), set by <see cref="Icons.IconStore"/> as it is registered; null: its default.
    public string? ChosenIcon { get; set; }

    /// The icon it shows: the chosen one, else its first suggestion, else its own <see cref="Icon"/>; null: its page's icon.
    public string? ShownIcon => ChosenIcon ?? Icons.IconLibrary.DefaultOf(SuggestedIcons, Icon);

    /// The feature the action belongs to: the title of the page that registers it.
    public string Category { get; }

    /// Optional part of the feature ("Hardware", "Software").
    public string? Group { get; }

    /// Optional part of the group ("Colour (RGB and gamma)"): the pickers show it in the group heading.
    public string? Subgroup { get; }

    /// Category, group and subgroup as one line ("Displays - Hardware - Presets"): what every list shows as the action's category.
    public string Heading { get; }

    public string Keywords { get; }

    /// A key combination the feature suggests for this action ("Ctrl+Alt+Up"), or null. It is never bound by itself: the Actions and Hotkeys window's
    /// "Suggest" binds it on request.
    public string? SuggestedKey { get; init; }

    /// The id of the set of actions whose <see cref="SuggestedKey"/>s are switched on and off together (<see cref="HotkeyRegistry.SetSuggestedKeys"/>, shown by
    /// <see cref="SuggestedKeysSwitch"/>); null: the action's suggested key is only offered by "Suggest". Set on the master entry, so there is no side list.
    public string? SuggestedKeySet { get; init; }

    /// The key combination the action starts with ("Alt+Z"), or null. <see cref="HotkeyRegistry"/> binds it when the action is first registered
    /// (never again once the user has given or cleared its keys, and not when another action already has it); "Restore defaults" puts it back.
    public string? DefaultKey { get; init; }

    /// The standard Windows shortcut that does the same in the window in front ("VolumeUp", "MediaPlayPause"; <c>KeyChord</c>'s names), or null.
    /// A mouse button or trackpad gesture sends it instead while a remote-input window is in front, so the other computer acts (<see cref="Actions.MouseActionRunner"/>).
    public string? KeyEquivalent { get; init; }

    /// True for the actions that keep the user in control (lock, open OmniControl, pause all rules): no automation block rule may stop
    /// them. A flag on the master entry, so there is no second list of ids.
    public bool NeverBlock { get; init; }

    /// True for an action another PC must confirm before it is run on this one (Restart, Shut down). A flag on the master entry, so there is no second list of ids.
    public bool ConfirmRun { get; init; }

    /// True for an action that may run only when a person presses its key, mouse button, tile or Run now ("Wake PC"): the run path refuses it
    /// from a rule, a chain or another PC (<see cref="HotkeyRegistry.Chain"/> set). A flag on the master entry, so there is no second list of ids.
    public bool PersonOnly { get; init; }

    /// True for the action that turns every screen off at once ("Turn off displays"): the all-screens master switch (here and on another PC's view) uses it when a screen has no power control of its own. A flag on the master entry, so there is no second list of ids.
    public bool ScreensOff { get; init; }

    /// The card of the basic set another PC sees that lists this action (<see cref="States.CommonGroups"/>), or null. Set by its owner on the master entry, so there is no side list.
    public string? Common { get; init; }

    /// The other PC this action runs on (the lists mark it "Other PCs · name"), set by its owner on the master entry; null for an action of this PC.
    public Shared.Services.RemotePcLabel? RemotePc { get; init; }

    /// The level this action moves, for a level action made with <see cref="ForLevel"/>; null for any other action.
    public HotkeyLevel? Level { get; private init; }

    /// +1 when this level action raises its level, -1 when it lowers it.
    public int LevelDirection { get; private init; }

    private readonly ActionFamily? _family;
    private readonly string? _familyRole;

    /// The family this action is one direction of (see <see cref="ActionFamily"/>); a level action is the family of its level, so it needs none set. Null: listed alone.
    public ActionFamily? Family
    {
        get => _family ?? (Level is { } level ? new ActionFamily(level.Id, level.Name) : null);
        init => _family = value;
    }

    /// The word for this action's direction inside its family ("Up", "Down", "Next", "On"); a level action says Up or Down by itself.
    public string? FamilyRole
    {
        get => _familyRole ?? (Level is null ? null : LevelDirection > 0 ? "Up" : "Down");
        init => _familyRole = value;
    }

    /// The id of the on/off state (<see cref="States.StateRegistry"/>) an action with the family role On or Off sets. The run path reads it first and does nothing
    /// when the thing is already there (<see cref="StateTarget"/>). Set on the master entry by the state's owner; null: the action just runs.
    public string? StateId { get; init; }

    /// For an action that works on something over the network (a smart device): how a rule waits for it and learns how a run went. Null: none.
    public ActionReach? Reach { get; init; }

    /// The part of the Choose-the-action window this action is listed under, when that is not the page that registers it (Power, Launch).
    public string? PickerSection { get; init; }

    /// The section the pickers list the action under: <see cref="PickerSection"/>, or its <see cref="Category"/>.
    public string Section => PickerSection ?? Category;

    /// Where the action's section stands in the pickers' section list and its icon; set by <see cref="HotkeyRegistry"/> from the live page list.
    public ActionSection? SectionPlace { get; set; }

    /// The group heading inside the section ("Hardware - Presets"): group and subgroup as one line; an action listed under another section than its
    /// own category (the apps, under Launch) takes the category as its heading; null when there is none or it only repeats the section.
    public string? PickerHeading =>
        (Shared.Services.Heading.Join(Group, Subgroup) is { Length: > 0 } heading ? heading : PickerSection is not null && PickerSection != Category ? Category : null) is { } line && line != Section ? line : null;

    /// The settings of its own the action takes from an input that runs it (a program to start, a countdown...); a level action always also takes its step (<see cref="Takes"/>).
    public ActionSettings Accepts { get; init; }

    /// What the pickers offer settings for.
    public ActionSettings Takes => Level is null ? Accepts : Accepts | ActionSettings.Step;

    /// How to run the action with the settings an input chose; null: it takes none beyond a level action's step.
    public Action<RunSettings>? RunConfigured { get; init; }

    /// A power action with a countdown: what is said while it counts down ("Sleeping") and once it ran out ("Going to sleep…"); a paired PC shows it.
    public CountdownWords? CountdownWords { get; init; }

    /// The action that cancels the power countdown showing now (another PC offers it beside its countdown).
    public bool CancelsCountdown { get; init; }

    /// The countdown, in seconds, this action runs with by its defaults (what a run from another PC uses); null when it takes none.
    public int? CountdownSeconds => Takes.HasFlag(ActionSettings.Countdown)
        ? (int)Timing.PowerCountdownOf(new OptionValues(Defaults).Int(Hotkeys.Actions.MouseOptions.Countdown)).TotalSeconds
        : null;

    /// For an action that can last while its input is held (repeated clicking): starts it with the settings the input chose and returns what ends it, or null when the settings say it does not last. Called on the input's thread, so it must return at once.
    public Func<RunSettings, Action?>? RunHeld { get; init; }

    /// Set: the pickers show "Remove" (or the hide wording) in this action's settings; it takes the action out of the master list (see <see cref="HotkeyRegistry.PickerActions"/>).
    public Action? Remove { get; init; }

    /// The words of the remove button ("Remove program", "Hide app").
    public string RemoveText { get; init; } = "Remove";

    /// Set: the user may rename what the action is about in the pickers (a program they added).
    public Renaming? Renaming { get; init; }

    /// Set: the pickers show these settings for the action and store each change at once (a custom action), or, with <see cref="ActionEditor.Create"/>, the form that makes one; see <see cref="ActionEditor"/>.
    public ActionEditor? Editor { get; init; }

    /// The Mouse and Trackpad picker lists its own version of this action (the layer actions, with their wrap and hold settings), so it leaves this one out.
    public bool CoveredByMousePicker { get; init; }

    /// Set: this is a button of its section's list in the pickers (add a program), not an action; it returns the actions it made, already registered.
    public Func<Avalonia.Visual, Task<IReadOnlyList<HotkeyAction>>>? Command { get; init; }

    /// For a <see cref="Command"/>: false hides the button for now ("Show hidden apps" while nothing is hidden).
    public Func<bool>? IsListed { get; init; }

    /// A small fact shown as a tag beside the entry's name in the Actions and Hotkeys window ("active"); null: none. Set again by the owner when the fact changes.
    public string? Tag { get; init; }

    /// A feature's own rule action ("Warm light fade to 3400 K"): a rule holding it belongs to the feature (its page, <see cref="Category"/>,
    /// and Automation's "From other pages" tab) and lasts while its triggers hold.
    public bool FeatureRule { get; init; }

    /// Set: the editor (a heading and a panel row) the Actions and Hotkeys window shows in this action's settings, besides its defaults (the power plan editor).
    public IReadOnlyList<ActionOption>? ManageSettings { get; init; }

    /// The names of the action's settings and their choices, so the Actions and Hotkeys window's search finds an action by what its picker offers.
    public string OptionWords => string.Join(" ", (ManageSettings ?? Editor?.Options ?? Array.Empty<ActionOption>()).SelectMany(o => (o.Choices ?? Array.Empty<string>()).Prepend(o.Label)));

    /// Called each time a picker listing this entry opens (the apps are looked for again).
    public Action? PickerOpened { get; init; }

    public bool HasDevices => _deviceSource is not null;

    /// "All displays" first, then one chip per device.
    public ObservableCollection<ScopeChip> Chips { get; } = new();

    /// The action's further keys after <see cref="HotkeyTarget.Gesture"/> (its first); an action may have several, each unique in the set.
    [ObservableProperty] private IReadOnlyList<string> _moreKeys = Array.Empty<string>();

    /// Every key of the action, the first one first; empty when it has none.
    public IReadOnlyList<string> Keys => Gesture.Length == 0 ? MoreKeys : MoreKeys.Prepend(Gesture).ToList();

    /// Gives the action exactly these keys (empty ones dropped, no key twice), with one save.
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

    /// The action's own settings that differ from their defaults (a program to start, how far a level moves, a countdown): what its hotkeys and
    /// rules run it with, and what a mouse button, gesture or rule step uses while it has no copy of its own. Kept with the keys, so a shortcut set holds them.
    public IReadOnlyDictionary<string, string> Defaults { get; private set; } = new Dictionary<string, string>();

    /// Takes these as the action's settings (only those that differ from the option's default are kept), with one save.
    public void SetDefaults(IReadOnlyDictionary<string, string> values)
    {
        Defaults = new Dictionary<string, string>(values);
        OnPropertyChanged(nameof(Defaults));
        Raise();
    }

    /// Why the action does nothing right now (a device it needs is missing), set and cleared live by the feature that owns it;
    /// null = it works. The Actions and Hotkeys window shows it on the action; the key can still be bound.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUnavailable))] private string? _unavailableReason;

    public bool IsUnavailable => UnavailableReason is not null;

    public bool ShowEmptyNote => HasDevices && Chips.Count <= 1;

    /// One-line summary of the scope: "All displays", a device name, or "2 of 3 displays".
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
        Heading = Shared.Services.Heading.Join(category, group, subgroup);
        Keywords = keywords;
        _run = run;
        _stepBy = stepBy;
        _deviceSource = devices;
        _noun = deviceNoun ?? "devices";
        _allText = $"All {_noun}";
        if (devices is not null) RefreshDevices();
    }

    /// A level action: moves <paramref name="level"/> by <see cref="DefaultStep"/> in <paramref name="direction"/> (+1 raise, -1 lower) when run,
    /// or by any amount a gesture asks for. <paramref name="step"/> gets the device ids (null = all) and the signed change in percent.
    /// Declaring the level here is all it takes for the gestures to offer it.
    public static HotkeyAction ForLevel(string id, string title, string category, string keywords, HotkeyLevel level, int direction,
        Action<IReadOnlyList<string>?, int> step, string? deviceNoun = null, Func<IReadOnlyList<HotkeyDevice>>? devices = null,
        string? group = null, string? subgroup = null, string? icon = null, string? suggestedKey = null, string? keyEquivalent = null) =>
        new(id, title, category, keywords, ids => step(ids, direction * DefaultStep), deviceNoun, devices,
            (ids, percent) => step(ids, direction * percent), group, subgroup)
        {
            Icon = icon,
            SuggestedKey = suggestedKey,
            KeyEquivalent = keyEquivalent,
            Level = level,
            LevelDirection = direction,
        };

    /// The id of the <see cref="Shared.Services.HardwareWatch"/> that keeps this action's device list (<see cref="HotkeyDevice"/>s) current. Set by the
    /// feature that owns the devices; when a picked device is missing at run time the registry refreshes that watch once and tries again.
    public string? HardwareWatch { get; set; }

    /// True when the action is limited to picked devices and one of them is not in the device list now.
    public bool HasMissingPick => HardwareWatch is not null && UseSelected && _deviceSource is { } source
        && _selectedIds.Except(source().Select(d => d.Id)).Any();

    /// Runs the action on every device, or only on the picked ones.
    public void Run() => _run(UseSelected ? _selectedIds.ToList() : null);

    /// Runs the action with the settings the input that fired it chose (others just run).
    public void RunWith(RunSettings settings)
    {
        if (RunConfigured is not null) RunConfigured(settings);
        else if (settings.Step is { } percent && Level is not null) RunBy(percent);
        else Run();
    }

    /// Moves the level by <paramref name="percent"/> in the action's own direction (a level action only; others just run).
    public void RunBy(int percent)
    {
        if (_stepBy is null) Run();
        else _stepBy(UseSelected ? _selectedIds.ToList() : null, percent);
    }

    /// Re-reads the available devices, keeping the user's picks.
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

    /// Takes the device choice of another direction of the same family (its settings are shared), with one save when it differs.
    public void CopyScopeFrom(HotkeyAction other)
    {
        if (!HasDevices || (UseSelected == other.UseSelected && _selectedIds.SetEquals(other._selectedIds))) return;
        _selectedIds.Clear();
        foreach (var id in other._selectedIds) _selectedIds.Add(id);
        UseSelected = other.UseSelected;
        RefreshDevices();
        Raise();
    }

    /// Picks exactly this option ("All displays" or one device) and clears the rest.
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

    /// "All" and device chips behave like one choice: All, or a set of devices.
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
