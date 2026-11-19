using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// State of the master picker, in three panes: sections (the app's pages and the few that are not pages, then Recent), the entries of the
/// section (or one list of matches from every section while a search is typed), and the settings of the highlighted entry, always shown and
/// live. Which line is highlighted and where the keyboard is live here; the view only draws them. Every list that is picked from (mouse
/// button actions, gesture actions, a rule's triggers, conditions and steps) is shown through this one model; a footer button, "Use this ...",
/// is the only confirm.
public sealed partial class PickerModel : ObservableObject
{
    /// The picker's keys (handled by PickerMenu), listed in the Keyboard shortcuts window of the pages that open it.
    public const string KeyHelp =
        "Type to search all sections; Up/Down choose an entry; Left/Right switch section; Tab moves into the entry's settings; Enter uses the entry; Esc clears the search, then closes. "
        + "In the settings: Up/Down choose a setting, Left/Right change it (Space flips a switch), Enter on a text setting edits it, Ctrl+Enter or a double-click types a number, "
        + "Enter anywhere else uses the entry; Esc, Tab or Shift+Tab go back to the list.";

    /// The last section: the entries used most recently, newest first.
    public const string RecentName = "Recent";
    private const string RecentIcon = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,5 A7,7 0 1 1 12,19 A7,7 0 1 1 12,5 Z M11,7 H13 V12.4 L16.2,14.3 L15.2,16 L11,13.5 Z";

    private const string ParameterKey = "$param"; // the text an entry takes (a shortcut, a program) is edited as one more setting, but is stored apart from them
    private const string ApplyToAllKey = "$all"; // the gesture window's "apply to every gesture using this action" switch, kept apart from the entry's settings
    private const string NameKey = "$name"; // the name of a program the user added, kept with the program, not with the input that runs it

    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    private List<PickGroup> _groups;
    private PickChoice? _current; // taken again from a list read anew (Follow), so it stays the same entry
    private IReadOnlyDictionary<string, string> _currentValues;
    private string _currentParameter;
    private readonly bool _offerApplyToAll;
    private readonly ISharedOptions? _shared;
    private IReadOnlyList<SectionRow>? _sections;
    private List<PickChoice>? _recentChoices;
    private PickChoice? _editing;
    private List<OptionItem> _allOptions = new(); // every setting of the entry, shown or not
    private bool _syncingEditor; // the settings are being shown again from what the entry stored; those are not edits

    /// What one entry is called ("action", "trigger", "condition"): the search box and the "Use this ..." button say it.
    public string Noun { get; init; } = ActionNoun;

    /// The <see cref="Noun"/> of a list of actions: only such a list has Recent and the "Only with a hotkey" filter.
    public const string ActionNoun = "action";

    /// True when the list is of actions (not triggers, conditions...): the "Only with a hotkey" filter and Recent belong to it only.
    public bool ListsActions => Noun == ActionNoun;

    /// The "Only with a hotkey" filter is shown: an action list in a window that shows the keys.
    public bool ShowsKeyFilter => HasKeys && ListsActions;

    public string SearchHint => !HasKeys ? $"Search {Noun}s" : $"Search {Noun}s and keys";

    /// The search box's tooltip (how to find an action by its key); none until the window gives the keys.
    public string? SearchTip => !HasKeys ? null : "Type a name or a key such as ctrl+o, or press the key combination (Ctrl+F)";
    public string UseText => ActiveInPanel ? AddAgainText : Target is { } target ? $"Use for {target}" : $"Use this {Noun}";

    /// The sections in pane 1: the list's own, then Recent when an action list has entries used before.
    public IReadOnlyList<SectionRow> Sections => _sections ??= BuildSections();

    // Read on first use, when Noun has been set (Recent is for action lists only).
    private List<PickChoice> RecentChoices => _recentChoices ??= ListsActions ? Recents.Matching(_groups.SelectMany(g => g.Choices)) : new();

    /// True while the Recent section is open.
    private bool InRecent => !IsSearching && SectionIndex >= _groups.Count && RecentChoices.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionHighlight))]
    private int _sectionIndex;

    /// The section the list shows as selected: none while a search runs (its matches come from every section), the open section otherwise.
    /// Choosing one leaves the search and opens it.
    public int SectionHighlight
    {
        get => IsSearching ? -1 : SectionIndex;
        set
        {
            if (value < 0) return; // the list reports "nothing selected" while it is reloading
            if (value == SectionIndex && IsSearching) Query = string.Empty;
            else SectionIndex = value;
        }
    }
    [ObservableProperty] private int _actionIndex;
    /// The lines of the list. One collection for the life of the window, changed through <see cref="ListDiff"/> and never swapped,
    /// so the list's recycled rows always draw the entry they hold now (a swapped list left a ghost row).
    public ObservableCollection<PickerRow> Actions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(SectionHighlight))]
    private string _query = string.Empty;

    // The settings pane.
    [ObservableProperty] private string _settingsTitle = string.Empty;

    /// One muted line when there is nothing to set: why the entry can't be used here, or that it has no settings.
    [ObservableProperty] private string _note = string.Empty;
    /// The one button after the settings: "Remove" for an entry that can be taken out, or the form's own button ("Create ...") for an entry that makes a new one.
    [ObservableProperty] private string _footText = "Remove";
    [ObservableProperty] private bool _hasFootButton;
    [ObservableProperty] private int _optionIndex = -1;

    /// True while the keyboard is in the settings pane (Up/Down choose a setting); false while it is in the list, where typing searches.
    [ObservableProperty] private bool _inSettings;

    /// True when the keyboard is on the button after the settings.
    [ObservableProperty] private bool _onFootButton;
    public ObservableCollection<OptionItem> OptionItems { get; } = new(); // the settings shown now

    /// True when the window should open with the keyboard in the settings (the card's cogwheel was used, not its pill).
    public bool StartInSettings { get; }

    /// Raised when a setting of the current entry is changed, so the change takes effect at once (without waiting for "Use this ...").
    public event Action<PickResult>? Live;

    public bool IsSearching => Query.Trim().Length > 0;

    /// The highlighted entry, or null when nothing matches.
    public PickChoice? ActiveChoice => ActiveRow is { } row ? MemberOf(row) : null;

    private PickerRow? ActiveRow => ActionIndex >= 0 && ActionIndex < Actions.Count ? Actions[ActionIndex] : null;

    /// True when the highlighted entry can be used (it is not greyed and not a list button).
    public bool CanUse => ActiveChoice is { Unavailable: null, Command: null, Create: null };

    public bool HasSettings => OptionItems.Count > 0 || HasFootButton;
    public bool HasNote => Note.Length > 0;

    public OptionItem? SelectedOption => OptionIndex >= 0 && OptionIndex < OptionItems.Count ? OptionItems[OptionIndex] : null;

    /// <param name="current">The entry the card or slot has now (highlighted when the window opens); null for an empty slot.</param>
    /// <param name="currentValues">The values of its settings.</param>
    /// <param name="currentParameter">The text the current entry takes (its shortcut, program or folder).</param>
    /// <param name="manage">Set for the Actions and hotkeys window opened to manage actions and their keys (no "Use this ..."); null to pick one.</param>
    /// <param name="offerApplyToAll">Add the switch that copies the settings to every other gesture using the same action (on by default).</param>
    /// <param name="startInSettings">Open with the keyboard in the settings of <paramref name="current"/> (from the card's cogwheel).</param>
    public PickerModel(IReadOnlyList<PickGroup> groups, PickChoice? current,
        IReadOnlyDictionary<string, string> currentValues, bool startInSettings = false, string currentParameter = "", bool offerApplyToAll = false,
        ManageMode? manage = null, ISharedOptions? shared = null)
    {
        _shared = shared;
        _manage = manage;
        _picking = manage is null;
        _groups = groups.Select(g => new PickGroup(g.Name, g.Choices.ToList(), g.Place)).ToList();
        _search = new SearchRunner<List<PickChoice>>(ShowMatches, "picker");
        foreach (var opened in groups.SelectMany(g => g.Choices).Select(c => c.Opened).OfType<Action>().Distinct()) opened();
        _current = current;
        _currentValues = currentValues;
        _currentParameter = currentParameter;
        _offerApplyToAll = offerApplyToAll;
        StartInSettings = startInSettings && current is not null;
        _sectionIndex = current is null ? 0 : Math.Max(0, _groups.FindIndex(g => g.Name == current.Category));
        var rows = Build();
        var at = current is null ? -1 : RowOf(rows, current);
        ShowRows(rows, () => at >= 0 ? at : FirstEntry());
    }

    private List<SectionRow> BuildSections()
    {
        var sections = _groups.Select(g => new SectionRow(g.Name, g.Place?.Icon)).ToList();
        if (RecentChoices.Count > 0) sections.Add(new SectionRow(RecentName, RecentIcon));
        return sections;
    }

    partial void OnSectionIndexChanged(int value)
    {
        if (value < 0 || _replacing) return; // the list reports "nothing selected" while it is reloading
        if (IsSearching) Query = string.Empty; // a click on a section leaves the search: its entries show again
        else Reload();
    }

    partial void OnQueryChanged(string value)
    {
        if (!IsSearching)
        {
            _search.Cancel();
            Reload();
            return;
        }
        var index = Index;
        _search.Request(token => index.Match(value, token));
    }

    partial void OnActionIndexChanged(int value)
    {
        if (!_replacing) SyncSettings();
    }

    private void Reload() => ShowRows(Build(), FirstEntry);

    /// The matches of a search that ran off the UI thread, shown (the filters are asked here, on the UI thread).
    private void ShowMatches(List<PickChoice> matches) => ShowRows(SearchRows(matches), FirstEntry);

    /// Shows <paramref name="rows"/> through <see cref="ListDiff"/> and highlights the line <paramref name="highlight"/> picks once they are in;
    /// the settings pane follows when <paramref name="sync"/>. The highlight is lifted while the rows change, so the list does not move it
    /// (and scroll) once per changed row.
    private void ShowRows(List<PickerRow> rows, Func<int> highlight, bool sync = true)
    {
        var replacing = _replacing;
        _replacing = true;
        ActionIndex = -1;
        ListDiff.Apply(Actions, rows, PickerRow.KeyOf, PickerRow.Same);
        ActionIndex = highlight();
        _replacing = replacing;
        if (sync) SyncSettings();
    }

    /// A search still waiting (typed within <see cref="Timing.SearchDebounce"/>) is shown now: a key that acts on the list sees the newest matches.
    public void Settle() => _search.Flush();

    /// The line of the entry with this <see cref="PickChoice.Spec"/> in <paramref name="rows"/>; -1 when it is not there.
    private static int IndexIn(IReadOnlyList<PickerRow> rows, string spec)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Choice?.Spec == spec || rows[i].Members?.Any(m => m.Spec == spec) == true) return i;
        return -1;
    }

    /// The first line that is an entry (headings are skipped); 0 when the list is empty.
    private int FirstEntry()
    {
        for (var i = 0; i < Actions.Count; i++)
            if (!Actions[i].IsHeading) return i;
        return 0;
    }

    /// Moves the highlight one line in the list (wrapping round).
    public void Move(int direction)
    {
        Settle();
        var at = ActionIndex;
        for (var i = 0; i < Actions.Count; i++)
        {
            at = Wrap.Index(at + direction, Actions.Count);
            if (!Actions[at].IsHeading) { ActionIndex = at; return; }
        }
    }

    /// Moves to the next or previous section (wrapping round); a search covers every section, so there it does nothing.
    public void MoveSection(int direction)
    {
        if (!IsSearching && Sections.Count > 0) SectionIndex = Wrap.Index(SectionIndex + direction, Sections.Count);
    }

    /// The result of "Use this ...": the highlighted entry with the values on the settings pane (settings that are hidden stay at their defaults).
    /// Null when it can't be used here (greyed, or a list's button).
    public PickResult? Use()
    {
        Settle();
        if (!CanUse || ActiveChoice is not { } choice) return null;
        return Gather(choice);
    }

    private PickResult Gather(PickChoice choice)
    {
        var settings = WithOwnCopy(_allOptions.Where(i => i.Option.Key is not (ParameterKey or ApplyToAllKey or NameKey or SameKey or DefaultsHeadingKey or DirectionKey) && !i.Option.Shared && i.Option.IsShown(ValueOf))
            .ToDictionary(i => i.Option.Key, i => i.Value));
        var applyToAll = _allOptions.FirstOrDefault(i => i.Option.Key == ApplyToAllKey)?.Value == "1";
        var parameter = _allOptions.FirstOrDefault(i => i.Option.Key == ParameterKey)?.Value;
        return new PickResult(choice, settings, parameter, applyToAll);
    }

    private string? ValueOf(string key) => _allOptions.FirstOrDefault(i => i.Option.Key == key)?.Value;

    /// Runs a list's button (add a program, show hidden ones) and puts what it made into that list, before the list's buttons.
    public async Task RunCommandAsync(PickChoice command, Avalonia.Visual from)
    {
        if (command.Command is null) return;
        var made = await command.Command(from);
        var at = _groups.FindIndex(g => g.Choices.Contains(command));
        if (at < 0) return;
        var list = _groups[at].Choices.ToList();
        var first = list.FindIndex(c => c.Command is not null);
        list.InsertRange(first < 0 ? list.Count : first, made.Where(m => !list.Contains(m)));
        _groups[at] = _groups[at] with { Choices = list };
        _index = null;
        Refresh(made.FirstOrDefault());
    }

    /// The button after the settings: a form makes its entry, any other entry is taken out.
    public void RunFootButton()
    {
        if (ActiveChoice?.Create is not null) CreateEntry();
        else Remove();
    }

    /// Takes the highlighted entry out of its list (and out of the master list, which its <see cref="PickChoice.Remove"/> does).
    private void Remove()
    {
        if (ActiveChoice is not { Remove: { } remove } choice) return;
        remove();
        _groups = _groups.Select(g => g with { Choices = g.Choices.Where(c => c != choice).ToList() }).ToList();
        _index = null;
        Refresh(null);
    }

    /// The highlighted form makes its entry from the values on the settings pane; the entry joins the form's list, before the form, and is highlighted.
    private void CreateEntry()
    {
        if (ActiveChoice is not { Create: { } create } form) return;
        var made = create(_allOptions.ToDictionary(i => i.Option.Key, i => i.Value));
        var at = _groups.FindIndex(g => g.Choices.Contains(form));
        if (at < 0) return;
        var list = _groups[at].Choices.ToList();
        list.Insert(list.IndexOf(form), made);
        _groups[at] = _groups[at] with { Choices = list };
        _index = null;
        Refresh(made);
    }

    private void Refresh(PickChoice? highlight)
    {
        var keep = ActionIndex;
        var rows = Build();
        var found = highlight is null ? -1 : RowOf(rows, highlight);
        var clamped = Math.Clamp(keep, 0, Math.Max(0, rows.Count - 1));
        ShowRows(rows, () => found >= 0 ? found : rows.Count > clamped && rows[clamped].IsHeading ? FirstEntry() : clamped);
    }

    // ---- The settings pane ----

    /// Shows the settings of the highlighted entry: its own values when it is the entry the card has now, its defaults otherwise.
    private void SyncSettings()
    {
        foreach (var old in _allOptions) old.PropertyChanged -= OnOptionValueChanged;
        _editing = null;
        SyncHotkeys();
        _allOptions = new List<OptionItem>();
        OptionItems.Clear();
        OptionIndex = -1;
        OnFootButton = false;
        OnPropertyChanged(nameof(CanUse));
        NotifyPanelChanged();
        if (ActiveChoice is not { } choice)
        {
            SettingsTitle = string.Empty;
            Note = Query.Length > 0 ? "Nothing matches." : string.Empty;
            HasFootButton = false;
            RaiseSettingsChanged();
            return;
        }

        SettingsTitle = TitleOf(choice);
        HasFootButton = (choice.Remove is not null || choice.Create is not null) && choice.Command is null;
        FootText = choice.Create is not null ? choice.Editor!.CreateText : choice.RemoveText;
        if (choice.Unavailable is not null || choice.Command is not null)
        {
            Note = choice.Unavailable ?? "Press Enter or click it to run it.";
            RaiseSettingsChanged();
            return;
        }

        _editing = choice;
        var values = ReadDefaults(choice, choice.Editor is { } editor ? editor.Read() : choice == _current ? _currentValues : NoValues);
        _allOptions = (choice.Options ?? Array.Empty<ActionOption>())
            .Select(o => new OptionItem(o, o.Shared && _shared is not null ? _shared.Read(o) : values.TryGetValue(o.Key, out var v) ? v : o.Default)).ToList();
        if (choice.ParamHint is not null)
            _allOptions.Insert(0, new OptionItem(ActionOption.Text(ParameterKey, choice.ParamLabel, "", choice.ParamHint) with { Browse = choice.ParamBrowse }, choice == _current ? _currentParameter : string.Empty));
        if (choice.Renaming is { } renaming)
            _allOptions.Insert(0, new OptionItem(ActionOption.Text(NameKey, "Name"), renaming.Current));
        AddDefaultsRow();
        AddDirectionRow(choice);
        if (_offerApplyToAll && choice.HasOptions) _allOptions.Add(new OptionItem(ActionOption.Toggle(ApplyToAllKey, "Apply these settings to every gesture that uses this action", on: true), "1"));
        foreach (var item in _allOptions) item.PropertyChanged += OnOptionValueChanged;
        SyncVisibleOptions();
        OptionIndex = OptionItems.Count > 0 ? NextOption(-1, 1) : -1;
        Note = OptionItems.Count == 0 && !HasFootButton && !HasHotkeys ? "Nothing to set here." : string.Empty;
        RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(HasNote));
    }

    private void OnOptionValueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OptionItem.Value) || sender is not OptionItem item || _syncingEditor) return;
        if (_editing is { Editor.Write: { } write, Create: null } editing)
        {
            if (write(item.Option.Key, item.Value) is { } title) Retitle(editing, title);
            ShowStored(editing);
            SyncVisibleOptions();
            return;
        }
        if (item.Option.Key == DirectionKey)
        {
            PickDirection(item.Value);
            return;
        }
        if (TakeDefaultsChange(item)) return;
        if (item.Option.Key == NameKey) Rename(item.Value);
        if (item.Option.Shared) _shared?.Write(item.Option, item.Value);
        SyncVisibleOptions();
        if (item.Option.Key == NameKey || _editing is null || _editing != _current) return;
        var pick = Gather(_editing);
        _currentValues = pick.Values;
        _currentParameter = pick.Parameter ?? string.Empty;
        Live?.Invoke(pick);
    }

    /// Shows again what an entry with an editor stored (a type change shows the other target box; a trimmed text reads trimmed).
    private void ShowStored(PickChoice choice)
    {
        var stored = choice.Editor!.Read();
        _syncingEditor = true;
        foreach (var item in _allOptions)
            if (stored.TryGetValue(item.Option.Key, out var value) && item.Value != value) item.Value = value;
        _syncingEditor = false;
    }

    /// The user gave the highlighted program another name: it is kept with the program and shown in the list at once.
    private void Rename(string name)
    {
        if (ActiveChoice is not { Renaming: { } renaming } choice || name.Trim().Length == 0) return;
        if (renaming.Apply(name) is not { } title) return;
        Retitle(choice, title, renaming with { Current = name.Trim() });
    }

    /// The entry has a new name: the list and the settings title show it at once.
    private void Retitle(PickChoice choice, string title, Renaming? renaming = null)
    {
        var renamed = choice with { Name = title, Renaming = renaming ?? choice.Renaming };
        _groups = _groups.Select(g => g with { Choices = g.Choices.Select(c => c == choice ? renamed : c).ToList() }).ToList();
        _index = null;
        SettingsTitle = renamed.Name;
        var rows = Actions.Select(r => r.Choice == choice ? Row(renamed, r.Hint) : r).ToList();
        var keep = ActionIndex;
        ShowRows(rows, () => keep, sync: false);
    }

    /// Shows only the settings whose condition holds (repeat speed only while repeating...), keeping the selected one selected.
    private void SyncVisibleOptions()
    {
        var selected = SelectedOption;
        var at = 0;
        foreach (var item in _allOptions)
        {
            item.Available = item.Option.IsAvailable(ValueOf);
            var shown = item.Option.IsShown(ValueOf);
            var present = at < OptionItems.Count && OptionItems[at] == item;
            if (shown && !present) OptionItems.Insert(at, item);
            else if (!shown && present) OptionItems.RemoveAt(at);
            if (shown) at++;
        }
        if (selected is not null && OptionItems.IndexOf(selected) is var index and >= 0 && index != OptionIndex && !OnFootButton) OptionIndex = index;
        RaiseSettingsChanged();
    }

    /// The keyboard moves into the settings (Tab); false when there is nothing to set.
    public bool EnterSettings()
    {
        if (OptionItems.Count == 0 && !HasFootButton) return false;
        InSettings = true;
        OnFootButton = OptionItems.Count == 0;
        OptionIndex = OnFootButton ? -1 : Math.Max(0, OptionIndex);
        return true;
    }

    /// The row after <paramref name="from"/> in this direction that is a setting, not a heading (-1 when there is none).
    private int NextOption(int from, int direction)
    {
        for (var i = from + direction; i >= 0 && i < OptionItems.Count; i += direction)
            if (!OptionItems[i].IsHeading) return i;
        return -1;
    }

    /// Up/Down walk the settings and then the remove button (when there is one), and loop.
    public void MoveOption(int direction)
    {
        var stops = OptionItems.Count + (HasFootButton ? 1 : 0);
        if (stops == 0) return;
        var at = OnFootButton ? OptionItems.Count : OptionIndex;
        var next = Wrap.Index(at + direction, stops);
        while (next < OptionItems.Count && OptionItems[next].IsHeading && next != at) next = Wrap.Index(next + direction, stops); // a heading holds no value: the keys pass it
        OnFootButton = next == OptionItems.Count;
        OptionIndex = OnFootButton ? -1 : next;
    }

    partial void OnOptionIndexChanged(int value)
    {
        if (value >= 0) OnFootButton = false; // a click on a setting takes the keyboard off the button
    }

    // ---- The list ----

    private List<PickerRow> Build()
    {
        if (_groups.Count == 0) return new List<PickerRow>();
        if (IsSearching) return SearchRows(Index.Match(Query));
        if (InRecent) return Rows(RecentChoices, c => c.Category, preselect: true);
        if (SectionIndex >= _groups.Count) return new List<PickerRow>();
        return InGroups(_groups[SectionIndex].Choices.Where(IsShown));
    }

    /// The entries of one section, those without a group heading first, then each group together under its heading row, the groups by name (one PC's group of Other PCs after another's); entries keep their registration order.
    private List<PickerRow> InGroups(IEnumerable<PickChoice> choices)
    {
        var rows = new List<PickerRow>();
        var all = choices.ToList();
        foreach (var heading in all.Select(c => c.Heading).Distinct().OrderBy(h => h is null ? 0 : 1).ThenBy(h => h, StringComparer.OrdinalIgnoreCase))
        {
            if (heading is not null) rows.Add(PickerRow.HeadingOf(heading));
            rows.AddRange(Rows(all.Where(c => c.Heading == heading), _ => null, preselect: false));
        }
        return rows;
    }

    /// The rows of a search's matches, each with its section as its hint.
    private List<PickerRow> SearchRows(IEnumerable<PickChoice> matches) => Rows(matches.Where(IsShown), c => c.Category, preselect: true);

    private bool IsShown(PickChoice choice) => (choice.IsListed?.Invoke() ?? true) && PassesKeyFilter(choice);

    // ---- The search ----

    private readonly SearchRunner<List<PickChoice>> _search;
    private SearchIndex<PickChoice>? _index;

    /// Every entry made ready for searching, on the first search after the list or its keys changed (they drop it by setting it to null).
    private SearchIndex<PickChoice> Index => _index ??= new SearchIndex<PickChoice>(_groups.SelectMany(g => g.Choices), SearchFieldsOf);

    /// A search matches an entry's name, its section, its group heading, its extra keywords (the shortcut, other names for it) and its keys ("ctrl+o").
    private IEnumerable<string?> SearchFieldsOf(PickChoice choice) =>
        new[] { choice.Name, choice.Category, choice.Heading, choice.Keywords }.Concat(KeysOf(choice) ?? Array.Empty<string>());
}
