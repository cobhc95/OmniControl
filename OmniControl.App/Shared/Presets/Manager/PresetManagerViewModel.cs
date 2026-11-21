using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Shared.Presets.Manager;

/// The one preset manager: the app's sidebar sections at the left (a section without presets
/// dimmed and not selectable), the chosen section's preset groups as cards at the right (one card per registered <see cref="PresetGroup"/>,
/// named as on its page), a search across every section's presets and shortcuts, one line for messages and the Undo of a delete.
/// Nothing here knows a kind of preset: the cards talk to the groups' providers. <see cref="Navigate"/> is where the window was opened from.
public sealed partial class PresetManagerViewModel : ObservableObject, IPresetCardHost, IDisposable
{
    public const string ApplyNote = "Changes apply at once.";

    private readonly PresetGroups _groups;
    private readonly PresetShortcuts _shortcuts;
    private readonly IReadOnlyList<PresetSection> _sections;
    private readonly List<PresetCardViewModel> _allCards = new();
    private DispatcherTimer? _undoTimer;
    private Action? _undo;

    /// <param name="sections">The app's sidebar pages, in their order.</param>
    public PresetManagerViewModel(PresetGroups groups, PresetShortcuts shortcuts, IReadOnlyList<PresetSection> sections)
    {
        // Order: the fields, then one card per group already registered, then the sections (which count the groups), then the
        // subscription for groups registered later, then the first section shown.
        (_groups, _shortcuts, _sections) = (groups, shortcuts, sections);
        foreach (var group in groups.All) _allCards.Add(new PresetCardViewModel(group, shortcuts, this));
        BuildSections();
        groups.Registered += OnRegistered;
        groups.Removed += OnRemoved;
        Navigate(null, null);
    }

    public ObservableCollection<PresetSectionItem> Sections { get; } = new();

    /// The cards shown: the chosen section's, or every card with a match while searching.
    public ObservableCollection<PresetCardViewModel> Cards { get; } = new();

    [ObservableProperty] private PresetSectionItem? _selectedSection;

    /// Part of a preset's name or shortcut, searched in every section.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSearching))] private string _search = string.Empty;

    [ObservableProperty] private string _heading = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string _message = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUndo))] private string _undoText = string.Empty;

    public bool IsSearching => Search.Trim().Length > 0;
    public bool HasMessage => Message.Length > 0;
    public bool HasUndo => UndoText.Length > 0;
    public bool NothingFound => IsSearching && Cards.Count == 0;

    /// The window scrolls this card into view (the card the manager was opened at).
    public event Action<PresetCardViewModel>? ScrollRequested;

    /// A row's name box opened: the window gives it the keyboard.
    public event Action<PresetRowViewModel>? RenameRequested;

    /// The window asks where to write a row's file.
    public event Action<PresetCardViewModel, PresetRowViewModel>? ExportFileRequested;

    /// The window asks which files to read into a card.
    public event Action<PresetCardViewModel>? ImportFileRequested;

    /// Shows a section and outlines one card in it: the group's own section when <paramref name="groupId"/> is known, else
    /// <paramref name="section"/>, else (or when that section has no presets) the first section that has some. The search is cleared.
    public void Navigate(string? section, string? groupId)
    {
        var target = groupId is null ? null : _allCards.FirstOrDefault(c => c.Id == groupId);
        var title = target?.Section ?? section;
        Search = string.Empty;
        SelectedSection = Sections.FirstOrDefault(s => s.Title == title && s.HasPresets) ?? Sections.FirstOrDefault(s => s.HasPresets);
        ShowCards();
        foreach (var card in _allCards) card.IsTarget = ReferenceEquals(card, target);
        if (target is not null) ScrollRequested?.Invoke(target);
    }

    partial void OnSelectedSectionChanged(PresetSectionItem? oldValue, PresetSectionItem? newValue)
    {
        if (newValue is { HasPresets: false })
        {
            SelectedSection = oldValue; // a dimmed section cannot be chosen
            return;
        }
        if (IsSearching) Search = string.Empty;
        else ShowCards();
    }

    partial void OnSearchChanged(string value) => ShowCards();

    private void ShowCards()
    {
        var query = Search.Trim();
        foreach (var card in _allCards) card.Filter(query, showSection: query.Length > 0);
        var shown = query.Length > 0
            ? _allCards.Where(c => !c.IsEmpty).ToList()
            : _allCards.Where(c => c.Section == SelectedSection?.Title).ToList();
        ListDiff.Apply(Cards, shown, c => c);
        var found = shown.Sum(c => c.Rows.Count);
        Heading = query.Length > 0 ? $"{found} {(found == 1 ? "preset" : "presets")} found" : SelectedSection?.Title ?? string.Empty;
        OnPropertyChanged(nameof(NothingFound));
    }

    private void BuildSections()
    {
        var chosen = SelectedSection?.Title;
        Sections.Clear();
        foreach (var section in _sections) Sections.Add(new PresetSectionItem(section, _groups.In(section.Title).Count));
        if (chosen is not null) SelectedSection = Sections.FirstOrDefault(s => s.Title == chosen);
    }

    private void OnRegistered(PresetGroup group)
    {
        _allCards.Add(new PresetCardViewModel(group, _shortcuts, this));
        BuildSections();
        ShowCards();
    }

    private void OnRemoved(PresetGroup group)
    {
        if (_allCards.FirstOrDefault(c => c.Id == group.Id) is not { } card) return;
        _allCards.Remove(card);
        card.Dispose();
        BuildSections();
        ShowCards();
    }

    // ---------------------------------------------------------------- what the cards ask

    void IPresetCardHost.Say(string? problem) => Message = problem ?? string.Empty;

    void IPresetCardHost.OfferUndo(string text, Action undo)
    {
        _undo = undo;
        UndoText = text;
        _undoTimer ??= MakeUndoTimer();
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private DispatcherTimer MakeUndoTimer()
    {
        var timer = new DispatcherTimer { Interval = Timing.PresetUndo };
        timer.Tick += (_, _) => ClearUndo();
        return timer;
    }

    void IPresetCardHost.RenameStarted(PresetRowViewModel row) => RenameRequested?.Invoke(row);

    void IPresetCardHost.ExportRequested(PresetCardViewModel card, PresetRowViewModel row) => ExportFileRequested?.Invoke(card, row);

    void IPresetCardHost.ImportRequested(PresetCardViewModel card) => ImportFileRequested?.Invoke(card);

    /// A file problem the window met (reading or writing a preset file).
    public void ReportFile(string text) => Message = text;

    [RelayCommand]
    private void Undo()
    {
        var undo = _undo;
        ClearUndo();
        undo?.Invoke();
    }

    private void ClearUndo()
    {
        _undoTimer?.Stop();
        _undo = null;
        UndoText = string.Empty;
    }

    public void Dispose()
    {
        _groups.Registered -= OnRegistered;
        _groups.Removed -= OnRemoved;
        _undoTimer?.Stop();
        foreach (var card in _allCards) card.Dispose();
    }
}
