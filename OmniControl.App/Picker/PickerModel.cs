using Avalonia.Media;
using System.Collections.ObjectModel;
using OmniControl.App.Hotkeys;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>What the picker returns: the chosen entry, the values of its settings, and (for an entry that takes text, such as a shortcut) that text; null when the entry takes none. ApplyToAll is true when the "apply to every gesture" switch was left on.</summary>
public sealed record PickResult(PickChoice Choice, IReadOnlyDictionary<string, string> Values, string? Parameter = null, bool ApplyToAll = false);

/// <summary>One entry of the sections pane: its name and its icon (a page's own; null where the list has none).</summary>
public sealed record SectionRow(string Name, string? Icon)
{
    public bool HasIcon => Icon is not null;
}

/// <summary>One line of the list: an entry (its text, and while searching or in Recent the section it belongs to, and why it can't be picked when it can't) or a group heading, which has no choice and is never highlighted.</summary>
public sealed class PickerRow
{
    public PickChoice? Choice { get; }
    public string Text { get; }
    public string? Hint { get; }
    public string? Unavailable => Choice?.Unavailable;
    public bool IsAvailable => Unavailable is null;

    public bool IsHeading => Choice is null;

    /// <summary>The entry's own glyph, or its section's icon when it has none; null for a heading.</summary>
    public Geometry? Icon => Choice is not { Command: null } ? null : IconResources.Of(Choice.Icon, Choice.Place?.Icon);

    public bool HasIcon => Icon is not null;

    /// <summary>The line is a button of its list (add a program), not an entry.</summary>
    public bool IsCommand => Choice?.Command is not null;
    public bool IsEntry => Choice is { Command: null };

    /// <summary>The entry's keys as keycaps, one row of parts per key (the Actions and hotkeys window); empty when it has none.</summary>
    public IReadOnlyList<IReadOnlyList<KeycapPart>> Keycaps { get; }

    /// <summary>The entry can have keys but has none: the row says "no key".</summary>
    public bool ShowsNoKey { get; }

    /// <summary>The entry is in the panel the picker was opened from (the tray flyout): the row says so in place of "no key".</summary>
    public bool InPanel { get; }

    public PickerRow(PickChoice choice, string text, string? hint, IReadOnlyList<string>? keys = null, bool inPanel = false)
    {
        Choice = choice;
        Text = text;
        Hint = hint;
        Keycaps = (keys ?? Array.Empty<string>()).Select(k => Hotkeys.Keycaps.From(k, string.Empty)).ToList();
        InPanel = inPanel;
        ShowsNoKey = keys is { Count: 0 } && !inPanel;
    }

    private PickerRow(string heading)
    {
        Text = heading;
        Keycaps = Array.Empty<IReadOnlyList<KeycapPart>>();
    }

    public static PickerRow HeadingOf(string heading) => new(heading);
}

/// <summary>
/// State of the master picker, in three panes: sections (the app's pages and the few that are not pages, then Recent), the entries of the
/// section (or one list of matches from every section while a search is typed), and the settings of the highlighted entry, always shown and
/// live. Which line is highlighted and where the keyboard is live here; the view only draws them. Every list that is picked from (mouse
/// button actions, gesture actions, a rule's triggers, conditions and steps) is shown through this one model; a footer button, "Use this ...",
/// is the only confirm.
/// </summary>
public sealed partial class PickerModel : ObservableObject
{
    /// <summary>The picker's keys (handled by PickerMenu), listed in the Keyboard shortcuts window of the pages that open it.</summary>
    public const string KeyHelp =
        "Type to search all sections; Up/Down choose an entry; Left/Right switch section; Tab moves into the entry's settings; Enter uses the entry; Esc clears the search, then closes. "
        + "In the settings: Up/Down choose a setting, Left/Right change it (Space flips a switch), Enter on a text setting edits it, Ctrl+Enter or a double-click types a number, "
        + "Enter anywhere else uses the entry; Esc, Tab or Shift+Tab go back to the list.";

    /// <summary>The last section: the entries used most recently, newest first.</summary>
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
    private IReadOnlyList<SectionRow>? _sections;
    private List<PickChoice>? _recentChoices;
    private PickChoice? _editing;
    private List<OptionItem> _allOptions = new(); // every setting of the entry, shown or not
    private bool _suppressSync;
    private bool _syncingEditor; // the settings are being shown again from what the entry stored; those are not edits

    /// <summary>What one entry is called ("action", "trigger", "condition"): the search box and the "Use this ..." button say it.</summary>
    public string Noun { get; init; } = ActionNoun;

    /// <summary>The <see cref="Noun"/> of a list of actions: only such a list has Recent and the "Only with a hotkey" filter.</summary>
    public const string ActionNoun = "action";

    /// <summary>True when the list is of actions (not triggers, conditions...): the "Only with a hotkey" filter and Recent belong to it only.</summary>
    public bool ListsActions => Noun == ActionNoun;

    /// <summary>The "Only with a hotkey" filter is shown: an action list in a window that shows the keys.</summary>
    public bool ShowsKeyFilter => HasKeys && ListsActions;

    public string SearchHint => !HasKeys ? $"Search {Noun}s" : $"Search {Noun}s and keys";

    /// <summary>The search box's tooltip (how to find an action by its key); none until the window gives the keys.</summary>
    public string? SearchTip => !HasKeys ? null : "Type a name or a key such as ctrl+o, or press the key combination (Ctrl+F)";
    public string UseText => ActiveInPanel ? AddAgainText : $"Use this {Noun}";

    /// <summary>The sections in pane 1: the list's own, then Recent when an action list has entries used before.</summary>
    public IReadOnlyList<SectionRow> Sections => _sections ??= BuildSections();

    // Read on first use, when Noun has been set (Recent is for action lists only).
    private List<PickChoice> RecentChoices => _recentChoices ??= ListsActions ? Recents.Matching(_groups.SelectMany(g => g.Choices)) : new();

    /// <summary>True while the Recent section is open.</summary>
    private bool InRecent => !IsSearching && SectionIndex >= _groups.Count && RecentChoices.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionHighlight))]
    private int _sectionIndex;

    /// <summary>The section the list shows as selected: none while a search runs (its matches come from every section), the open section otherwise.
    /// Choosing one leaves the search and opens it.</summary>
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
    [ObservableProperty] private IReadOnlyList<PickerRow> _actions = Array.Empty<PickerRow>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(SectionHighlight))]
    private string _query = string.Empty;

    // The settings pane.
    [ObservableProperty] private string _settingsTitle = string.Empty;

    /// <summary>One muted line when there is nothing to set: why the entry can't be used here, or that it has no settings.</summary>
    [ObservableProperty] private string _note = string.Empty;
    /// <summary>The one button after the settings: "Remove" for an entry that can be taken out, or the form's own button ("Create ...") for an entry that makes a new one.</summary>
    [ObservableProperty] private string _footText = "Remove";
    [ObservableProperty] private bool _hasFootButton;
    [ObservableProperty] private int _optionIndex = -1;

    /// <summary>True while the keyboard is in the settings pane (Up/Down choose a setting); false while it is in the list, where typing searches.</summary>
    [ObservableProperty] private bool _inSettings;

    /// <summary>True when the keyboard is on the button after the settings.</summary>
    [ObservableProperty] private bool _onFootButton;
    public ObservableCollection<OptionItem> OptionItems { get; } = new(); // the settings shown now

    /// <summary>True when the window should open with the keyboard in the settings (the card's cogwheel was used, not its pill).</summary>
    public bool StartInSettings { get; }

    /// <summary>Raised when a setting of the current entry is changed, so the change takes effect at once (without waiting for "Use this ...").</summary>
    public event Action<PickResult>? Live;

    public bool IsSearching => Query.Trim().Length > 0;

    /// <summary>The highlighted entry, or null when nothing matches.</summary>
    public PickChoice? ActiveChoice => ActionIndex >= 0 && ActionIndex < Actions.Count ? Actions[ActionIndex].Choice : null;

    /// <summary>True when the highlighted entry can be used (it is not greyed and not a list button).</summary>
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
        ManageMode? manage = null)
    {
        _manage = manage;
        _picking = manage is null;
        _groups = groups.Select(g => new PickGroup(g.Name, g.Choices.ToList(), g.Place)).ToList();
        foreach (var opened in groups.SelectMany(g => g.Choices).Select(c => c.Opened).OfType<Action>().Distinct()) opened();
        _current = current;
        _currentValues = currentValues;
        _currentParameter = currentParameter;
        _offerApplyToAll = offerApplyToAll;
        StartInSettings = startInSettings && current is not null;
        _sectionIndex = current is null ? 0 : Math.Max(0, _groups.FindIndex(g => g.Name == current.Category));
        Actions = Build();
        var at = current is null ? -1 : Actions.ToList().FindIndex(a => a.Choice == current);
        _actionIndex = at >= 0 ? at : FirstEntry();
        SyncSettings();
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

    partial void OnQueryChanged(string value) => Reload();

    partial void OnActionIndexChanged(int value)
    {
        if (!_replacing) SyncSettings();
    }

    partial void OnActionsChanged(IReadOnlyList<PickerRow> value)
    {
        if (!_suppressSync && !_replacing) SyncSettings();
    }

    private void Reload()
    {
        Actions = Build();
        ActionIndex = FirstEntry();
    }

    /// <summary>The first line that is an entry (headings are skipped); 0 when the list is empty.</summary>
    private int FirstEntry() => Math.Max(0, Actions.ToList().FindIndex(r => !r.IsHeading));

    /// <summary>Moves the highlight one line in the list (wrapping round).</summary>
    public void Move(int direction)
    {
        var at = ActionIndex;
        for (var i = 0; i < Actions.Count; i++)
        {
            at = Wrap.Index(at + direction, Actions.Count);
            if (!Actions[at].IsHeading) { ActionIndex = at; return; }
        }
    }

    /// <summary>Moves to the next or previous section (wrapping round); a search covers every section, so there it does nothing.</summary>
    public void MoveSection(int direction)
    {
        if (!IsSearching && Sections.Count > 0) SectionIndex = Wrap.Index(SectionIndex + direction, Sections.Count);
    }

    /// <summary>
    /// The result of "Use this ...": the highlighted entry with the values on the settings pane (settings that are hidden stay at their defaults).
    /// Null when it can't be used here (greyed, or a list's button).
    /// </summary>
    public PickResult? Use()
    {
        if (!CanUse || ActiveChoice is not { } choice) return null;
        return Gather(choice);
    }

    private PickResult Gather(PickChoice choice)
    {
        var settings = _allOptions.Where(i => i.Option.Key is not (ParameterKey or ApplyToAllKey or NameKey) && i.Option.IsShown(ValueOf)).ToDictionary(i => i.Option.Key, i => i.Value);
        var applyToAll = _allOptions.FirstOrDefault(i => i.Option.Key == ApplyToAllKey)?.Value == "1";
        var parameter = _allOptions.FirstOrDefault(i => i.Option.Key == ParameterKey)?.Value;
        return new PickResult(choice, settings, parameter, applyToAll);
    }

    private string? ValueOf(string key) => _allOptions.FirstOrDefault(i => i.Option.Key == key)?.Value;

    /// <summary>Runs a list's button (add a program, show hidden ones) and puts what it made into that list, before the list's buttons.</summary>
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
        Refresh(made.FirstOrDefault());
    }

    /// <summary>The button after the settings: a form makes its entry, any other entry is taken out.</summary>
    public void RunFootButton()
    {
        if (ActiveChoice?.Create is not null) CreateEntry();
        else Remove();
    }

    /// <summary>Takes the highlighted entry out of its list (and out of the master list, which its <see cref="PickChoice.Remove"/> does).</summary>
    private void Remove()
    {
        if (ActiveChoice is not { Remove: { } remove } choice) return;
        remove();
        _groups = _groups.Select(g => g with { Choices = g.Choices.Where(c => c != choice).ToList() }).ToList();
        Refresh(null);
    }

    /// <summary>The highlighted form makes its entry from the values on the settings pane; the entry joins the form's list, before the form, and is highlighted.</summary>
    private void CreateEntry()
    {
        if (ActiveChoice is not { Create: { } create } form) return;
        var made = create(_allOptions.ToDictionary(i => i.Option.Key, i => i.Value));
        var at = _groups.FindIndex(g => g.Choices.Contains(form));
        if (at < 0) return;
        var list = _groups[at].Choices.ToList();
        list.Insert(list.IndexOf(form), made);
        _groups[at] = _groups[at] with { Choices = list };
        Refresh(made);
    }

    private void Refresh(PickChoice? highlight)
    {
        var keep = ActionIndex;
        Actions = Build();
        var found = highlight is null ? -1 : Actions.ToList().FindIndex(a => a.Choice == highlight);
        var clamped = Math.Clamp(keep, 0, Math.Max(0, Actions.Count - 1));
        ActionIndex = found >= 0 ? found : Actions.Count > clamped && Actions[clamped].IsHeading ? FirstEntry() : clamped;
    }

    // ---- The settings pane ----

    /// <summary>Shows the settings of the highlighted entry: its own values when it is the entry the card has now, its defaults otherwise.</summary>
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

        SettingsTitle = choice.Name;
        HasFootButton = (choice.Remove is not null || choice.Create is not null) && choice.Command is null;
        FootText = choice.Create is not null ? choice.Editor!.CreateText : choice.RemoveText;
        if (choice.Unavailable is not null || choice.Command is not null)
        {
            Note = choice.Unavailable ?? "Press Enter or click it to run it.";
            RaiseSettingsChanged();
            return;
        }

        _editing = choice;
        var values = choice.Editor is { } editor ? editor.Read() : choice == _current ? _currentValues : NoValues;
        _allOptions = (choice.Options ?? Array.Empty<ActionOption>())
            .Select(o => new OptionItem(o, values.TryGetValue(o.Key, out var v) ? v : o.Default)).ToList();
        if (choice.ParamHint is not null)
            _allOptions.Insert(0, new OptionItem(ActionOption.Text(ParameterKey, choice.ParamLabel, "", choice.ParamHint) with { Browse = choice.ParamBrowse }, choice == _current ? _currentParameter : string.Empty));
        if (choice.Renaming is { } renaming)
            _allOptions.Insert(0, new OptionItem(ActionOption.Text(NameKey, "Name"), renaming.Current));
        if (_offerApplyToAll && choice.HasOptions) _allOptions.Add(new OptionItem(ActionOption.Toggle(ApplyToAllKey, "Apply these settings to every gesture that uses this action", on: true), "1"));
        foreach (var item in _allOptions) item.PropertyChanged += OnOptionValueChanged;
        SyncVisibleOptions();
        OptionIndex = OptionItems.Count > 0 ? 0 : -1;
        Note = OptionItems.Count == 0 && !HasFootButton && Hotkeys is null ? "Nothing to set here." : string.Empty;
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
        if (item.Option.Key == NameKey) Rename(item.Value);
        SyncVisibleOptions();
        if (item.Option.Key == NameKey || _editing is null || _editing != _current) return;
        var pick = Gather(_editing);
        _currentValues = pick.Values;
        _currentParameter = pick.Parameter ?? string.Empty;
        Live?.Invoke(pick);
    }

    /// <summary>Shows again what an entry with an editor stored (a type change shows the other target box; a trimmed text reads trimmed).</summary>
    private void ShowStored(PickChoice choice)
    {
        var stored = choice.Editor!.Read();
        _syncingEditor = true;
        foreach (var item in _allOptions)
            if (stored.TryGetValue(item.Option.Key, out var value) && item.Value != value) item.Value = value;
        _syncingEditor = false;
    }

    /// <summary>The user gave the highlighted program another name: it is kept with the program and shown in the list at once.</summary>
    private void Rename(string name)
    {
        if (ActiveChoice is not { Renaming: { } renaming } choice || name.Trim().Length == 0) return;
        if (renaming.Apply(name) is not { } title) return;
        Retitle(choice, title, renaming with { Current = name.Trim() });
    }

    /// <summary>The entry has a new name: the list and the settings title show it at once.</summary>
    private void Retitle(PickChoice choice, string title, Renaming? renaming = null)
    {
        var renamed = choice with { Name = title, Renaming = renaming ?? choice.Renaming };
        _groups = _groups.Select(g => g with { Choices = g.Choices.Select(c => c == choice ? renamed : c).ToList() }).ToList();
        SettingsTitle = renamed.Name;
        var rows = Actions.Select(r => r.Choice == choice ? Row(renamed, r.Hint) : r).ToList();
        _suppressSync = true;
        Actions = rows;
        _suppressSync = false;
    }

    /// <summary>Shows only the settings whose condition holds (repeat speed only while repeating...), keeping the selected one selected.</summary>
    private void SyncVisibleOptions()
    {
        var selected = SelectedOption;
        var at = 0;
        foreach (var item in _allOptions)
        {
            var shown = item.Option.IsShown(ValueOf);
            var present = at < OptionItems.Count && OptionItems[at] == item;
            if (shown && !present) OptionItems.Insert(at, item);
            else if (!shown && present) OptionItems.RemoveAt(at);
            if (shown) at++;
        }
        if (selected is not null && OptionItems.IndexOf(selected) is var index and >= 0 && index != OptionIndex && !OnFootButton) OptionIndex = index;
        RaiseSettingsChanged();
    }

    /// <summary>The keyboard moves into the settings (Tab); false when there is nothing to set.</summary>
    public bool EnterSettings()
    {
        if (OptionItems.Count == 0 && !HasFootButton) return false;
        InSettings = true;
        OnFootButton = OptionItems.Count == 0;
        OptionIndex = OnFootButton ? -1 : Math.Max(0, OptionIndex);
        return true;
    }

    /// <summary>Up/Down walk the settings and then the remove button (when there is one), and loop.</summary>
    public void MoveOption(int direction)
    {
        var stops = OptionItems.Count + (HasFootButton ? 1 : 0);
        if (stops == 0) return;
        var at = OnFootButton ? OptionItems.Count : OptionIndex;
        var next = Wrap.Index(at + direction, stops);
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
        if (IsSearching)
        {
            var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return _groups.SelectMany(g => g.Choices)
                .Where(c => IsShown(c) && words.All(w => Matches(c, w) || KeyMatches(c, w)))
                .Select(c => Row(c, c.Category)).ToList();
        }
        if (InRecent) return RecentChoices.Select(c => Row(c, c.Category)).ToList();
        if (SectionIndex >= _groups.Count) return new List<PickerRow>();
        return InGroups(_groups[SectionIndex].Choices.Where(IsShown));
    }

    /// <summary>The entries of one section, those without a group heading first, then each group together under its heading row.</summary>
    private List<PickerRow> InGroups(IEnumerable<PickChoice> choices)
    {
        var rows = new List<PickerRow>();
        var all = choices.ToList();
        foreach (var heading in all.Select(c => c.Heading).Distinct().OrderBy(h => h is null ? 0 : 1))
        {
            if (heading is not null) rows.Add(PickerRow.HeadingOf(heading));
            rows.AddRange(all.Where(c => c.Heading == heading).Select(c => Row(c, null)));
        }
        return rows;
    }

    private bool IsShown(PickChoice choice) => (choice.IsListed?.Invoke() ?? true) && PassesKeyFilter(choice);

    /// <summary>A search word matches an entry's name, its section, its group heading, or its extra keywords (the shortcut, other names for it).</summary>
    private static bool Matches(PickChoice choice, string word) =>
        TextMatch.Any(word, choice.Name, choice.Category, choice.Heading ?? string.Empty, choice.Keywords);
}
