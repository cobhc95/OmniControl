using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>What the picker returns: the chosen entry, the values of its settings, and (for an entry that takes text, such as a shortcut) that text; null when the settings page was not used. ApplyToAll is true when the "apply to every gesture" switch was left on.</summary>
public sealed record PickResult(PickChoice Choice, IReadOnlyDictionary<string, string> Values, string? Parameter = null, bool ApplyToAll = false);

/// <summary>One line of the list: the text, (while searching) the category it belongs to, the cogwheel when the entry has settings, and why it can't be picked (greyed) when it can't.</summary>
public sealed partial class PickerRow : ObservableObject
{
    public PickChoice Choice { get; }
    public string Text { get; }
    public string? Hint { get; }
    public bool HasOptions => Choice.HasOptions && IsAvailable;
    public string? Unavailable => Choice.Unavailable;
    public bool IsAvailable => Choice.Unavailable is null;

    /// <summary>True while the keyboard is on this row's cogwheel.</summary>
    [ObservableProperty] private bool _isCogActive;

    public PickerRow(PickChoice choice, string text, string? hint)
    {
        Choice = choice;
        Text = text;
        Hint = hint;
    }
}

/// <summary>
/// State of the master picker: categories on the left, their entries on the right (or one list of matches while a search is typed),
/// and, for an entry that has settings, a second page with those settings. Which line is highlighted and which side the arrow keys
/// move in live here; the view only draws them. Every list that is picked from (mouse button actions, gesture actions, a rule's
/// triggers, conditions and steps) is shown through this one model.
/// </summary>
public sealed partial class PickerModel : ObservableObject
{
    /// <summary>The picker's keys (handled by PickerMenu), listed in the Keyboard shortcuts window of the pages that open it.</summary>
    public const string KeyHelp =
        "Type to search; Up/Down choose; Left/Right switch between categories, entries and an entry's cogwheel; Enter picks (on a cogwheel: opens its settings); "
        + "Tab opens the highlighted entry's settings; Esc closes. On the settings page: Up/Down choose a setting, Left/Right change it (Space flips a switch); "
        + "Enter on a text setting edits it; Ctrl+Enter or a double-click types a number; Enter anywhere else uses the action with these settings; Esc goes back.";

    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    private readonly IReadOnlyList<PickGroup> _groups;
    private readonly PickChoice? _current;
    private readonly IReadOnlyDictionary<string, string> _currentValues;
    private readonly string _currentParameter;
    private const string ParameterKey = "$param"; // the text an entry takes (a shortcut, a program) is edited as one more setting, but is stored apart from them
    private const string ApplyToAllKey = "$all"; // the gesture window's "apply to every gesture using this action" switch, kept apart from the entry's settings
    private readonly bool _offerApplyToAll;
    private PickChoice? _editing;

    public IReadOnlyList<string> Categories { get; }

    /// <summary>What one entry is called ("action", "trigger", "condition"): the search box and the "Use this ..." button say it.</summary>
    public string Noun { get; init; } = "action";

    /// <summary>True: picking an entry that needs text or settings (a program, a command: <see cref="PickChoice.NeedsSettings"/>) opens its settings page first, so they are given here.</summary>
    public bool AskSettingsFirst { get; init; }

    public string SearchHint => $"Search {Noun}s";
    public string UseText => $"Use this {Noun}";

    [ObservableProperty] private int _categoryIndex;
    [ObservableProperty] private int _actionIndex;
    [ObservableProperty] private IReadOnlyList<PickerRow> _actions = Array.Empty<PickerRow>();

    /// <summary>True when Up/Down move through the categories instead of the entries.</summary>
    [ObservableProperty] private bool _inCategories;

    /// <summary>True when the keyboard is on the highlighted entry's cogwheel (Enter opens its settings).</summary>
    [ObservableProperty] private bool _inCog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _query = string.Empty;

    // The settings page.
    [ObservableProperty] private bool _isEditingOptions;
    [ObservableProperty] private int _optionIndex;

    /// <summary>True when the keyboard is on the "Use this ..." button (Enter uses the entry).</summary>
    [ObservableProperty] private bool _onUseButton;
    [ObservableProperty] private string _optionsTitle = string.Empty;
    public ObservableCollection<OptionItem> OptionItems { get; } = new(); // the settings shown now

    /// <summary>Raised when a setting of the current entry is changed, so the change takes effect at once (without waiting for "Use this ...").</summary>
    public event Action<PickResult>? Live;
    private List<OptionItem> _allOptions = new();                          // every setting of the entry, shown or not

    public bool IsSearching => Query.Trim().Length > 0;

    /// <summary>The highlighted entry, or null when nothing matches.</summary>
    public PickChoice? ActiveChoice => ActionIndex >= 0 && ActionIndex < Actions.Count ? Actions[ActionIndex].Choice : null;

    /// <summary>True when the highlighted entry has settings to open.</summary>
    public bool CanOpenOptions => ActiveChoice is { HasOptions: true, Unavailable: null };

    public OptionItem? SelectedOption => OptionIndex >= 0 && OptionIndex < OptionItems.Count ? OptionItems[OptionIndex] : null;

    /// <param name="current">The entry the card or slot has now (highlighted when the window opens); null for an empty slot.</param>
    /// <param name="currentValues">The values of its settings.</param>
    /// <param name="currentParameter">The text the current entry takes (its shortcut, program or folder).</param>
    /// <param name="offerApplyToAll">Add the switch that copies the settings to every other gesture using the same action (on by default).</param>
    /// <param name="startInOptions">Open straight on the settings of <paramref name="current"/> (from the card's cogwheel).</param>
    public PickerModel(IReadOnlyList<PickGroup> groups, PickChoice? current,
        IReadOnlyDictionary<string, string> currentValues, bool startInOptions = false, string currentParameter = "", bool offerApplyToAll = false)
    {
        _groups = groups;
        _current = current;
        _currentValues = currentValues;
        _currentParameter = currentParameter;
        _offerApplyToAll = offerApplyToAll;
        Categories = groups.Select(g => g.Name).ToList();
        _categoryIndex = current is null ? 0 : Math.Max(0, Categories.ToList().IndexOf(current.Category));
        Actions = Build();
        _actionIndex = Math.Max(0, Actions.ToList().FindIndex(a => a.Choice == current));
        if (startInOptions && current is { HasOptions: true }) OpenOptionsFor(current);
    }

    partial void OnCategoryIndexChanged(int value)
    {
        if (value < 0) return; // the list reports "nothing selected" while it is reloading
        Actions = Build();
        ActionIndex = 0;
    }

    partial void OnQueryChanged(string value)
    {
        Actions = Build();
        ActionIndex = 0;
    }

    partial void OnActionIndexChanged(int value) => RefreshCog();

    partial void OnActionsChanged(IReadOnlyList<PickerRow> value) => RefreshCog();

    partial void OnInCogChanged(bool value) => RefreshCog();

    private void RefreshCog()
    {
        if (InCog && !CanOpenOptions)
        {
            InCog = false; // the highlight moved to an entry without settings
            return;
        }
        for (var i = 0; i < Actions.Count; i++) Actions[i].IsCogActive = InCog && i == ActionIndex;
    }

    /// <summary>Moves the highlight one line (wrapping round) in the side the keyboard is on.</summary>
    public void Move(int direction)
    {
        InCog = false;
        if (InCategories && !IsSearching) CategoryIndex = Wrap.Index(CategoryIndex + direction, Categories.Count);
        else if (Actions.Count > 0) ActionIndex = Wrap.Index(ActionIndex + direction, Actions.Count);
    }

    /// <summary>
    /// The result of choosing <paramref name="choice"/> from the list: the same settings as now when it is the current entry, otherwise
    /// its defaults. Null when it can't be picked here, or when its text is asked for first (its settings page opens instead).
    /// </summary>
    public PickResult? PickFor(PickChoice choice)
    {
        if (choice.Unavailable is not null) return null;
        if (AskSettingsFirst && choice.NeedsSettings)
        {
            OpenOptionsFor(choice);
            return null;
        }
        return new PickResult(choice, choice == _current ? _currentValues : NoValues, choice == _current ? _currentParameter : null);
    }

    // ---- The settings page ----

    public void OpenOptions()
    {
        if (CanOpenOptions) OpenOptionsFor(ActiveChoice!);
    }

    public void OpenOptionsFor(PickChoice choice)
    {
        if (choice.Unavailable is not null) return;
        _editing = choice;
        var values = choice == _current ? _currentValues : NoValues;
        OptionItems.Clear();
        foreach (var old in _allOptions) old.PropertyChanged -= OnOptionValueChanged;
        _allOptions = (choice.Options ?? Array.Empty<ActionOption>())
            .Select(o => new OptionItem(o, values.TryGetValue(o.Key, out var v) ? v : o.Default)).ToList();
        if (choice.ParamHint is not null)
            _allOptions.Insert(0, new OptionItem(ActionOption.Text(ParameterKey, choice.ParamLabel, "", choice.ParamHint), choice == _current ? _currentParameter : string.Empty));
        if (_offerApplyToAll) _allOptions.Add(new OptionItem(ActionOption.Toggle(ApplyToAllKey, "Apply these settings to every gesture that uses this action", on: true), "1"));
        foreach (var item in _allOptions) item.PropertyChanged += OnOptionValueChanged;
        SyncVisibleOptions();
        OptionIndex = 0;
        OnUseButton = false;
        OptionsTitle = $"{choice.Name}: settings";
        IsEditingOptions = true;
    }

    public void CloseOptions()
    {
        IsEditingOptions = false;
        _editing = null;
    }

    /// <summary>The entry being edited, with the values on the page (settings that are hidden stay at their defaults), or null when the page is not open.</summary>
    public PickResult? PickFromOptions()
    {
        if (_editing is null) return null;
        var settings = OptionItems.Where(i => i.Option.Key is not (ParameterKey or ApplyToAllKey)).ToDictionary(i => i.Option.Key, i => i.Value);
        var applyToAll = OptionItems.FirstOrDefault(i => i.Option.Key == ApplyToAllKey)?.Value == "1";
        return new PickResult(_editing, settings, OptionItems.FirstOrDefault(i => i.Option.Key == ParameterKey)?.Value, applyToAll);
    }

    private void OnOptionValueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OptionItem.Value)) return;
        SyncVisibleOptions();
        if (_editing is not null && _editing == _current && PickFromOptions() is { } pick) Live?.Invoke(pick);
    }

    /// <summary>Shows only the settings whose condition holds (repeat speed only while repeating...), keeping the selected one selected.</summary>
    private void SyncVisibleOptions()
    {
        var selected = SelectedOption;
        string? ValueOf(string key) => _allOptions.FirstOrDefault(i => i.Option.Key == key)?.Value;
        var at = 0;
        foreach (var item in _allOptions)
        {
            var shown = item.Option.IsShown(ValueOf);
            var present = at < OptionItems.Count && OptionItems[at] == item;
            if (shown && !present) OptionItems.Insert(at, item);
            else if (!shown && present) OptionItems.RemoveAt(at);
            if (shown) at++;
        }
        if (selected is not null && OptionItems.IndexOf(selected) is var index and >= 0 && index != OptionIndex && !OnUseButton) OptionIndex = index;
    }

    /// <summary>Up/Down walk the settings and then the "Use this ..." button, and loop.</summary>
    public void MoveOption(int direction)
    {
        var stops = OptionItems.Count + 1; // the last stop is the button
        var at = OnUseButton ? OptionItems.Count : OptionIndex;
        var next = Wrap.Index(at + direction, stops);
        OnUseButton = next == OptionItems.Count;
        OptionIndex = OnUseButton ? -1 : next;
    }

    partial void OnOptionIndexChanged(int value)
    {
        if (value >= 0) OnUseButton = false; // a click on a setting takes the keyboard off the button
    }

    private List<PickerRow> Build()
    {
        if (_groups.Count == 0) return new List<PickerRow>();
        if (!IsSearching)
            return _groups[CategoryIndex].Choices.Select(c => new PickerRow(c, c.Name, null)).ToList();

        var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _groups.SelectMany(g => g.Choices)
            .Where(c => words.All(w => Matches(c, w)))
            .Select(c => new PickerRow(c, c.Name, c.Category)).ToList();
    }

    /// <summary>A search word matches an entry's name, its category, or its extra keywords (the shortcut, other names for it).</summary>
    private static bool Matches(PickChoice choice, string word) =>
        TextMatch.Any(word, choice.Name, choice.Category, choice.Keywords);
}
