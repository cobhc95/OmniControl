using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Shared.Presets.Manager;

/// What a card asks of the preset manager around it: the message line, the Undo line, and the view's jobs (focus a name box, a file).
internal interface IPresetCardHost
{
    /// One plain sentence on the window's line (null clears it).
    void Say(string? problem);

    void OfferUndo(string text, Action undo);

    void RenameStarted(PresetRowViewModel row);

    void ExportRequested(PresetCardViewModel card, PresetRowViewModel row);

    void ImportRequested(PresetCardViewModel card);
}

/// One preset group's card in the preset manager (a <see cref="PresetGroup"/> of the master list): its presets as rows in their order,
/// the add button at its foot, the group's own switch, and, while the window searches, only the presets that match. Every change goes to
/// the group's provider at once (principle 2); the rows follow the provider's <see cref="IPresetProvider.Changed"/> and the shortcuts'.
public sealed partial class PresetCardViewModel : ObservableObject, IPresetRowOwner, IDisposable
{
    private readonly PresetGroup _group;
    private readonly PresetShortcuts _shortcuts;
    private readonly IPresetCardHost _host;
    private string _filter = string.Empty;

    internal PresetCardViewModel(PresetGroup group, PresetShortcuts shortcuts, IPresetCardHost host)
    {
        // Order: the fields first, then the subscriptions, then the first fill (which reads the provider and the shortcuts).
        (_group, _shortcuts, _host) = (group, shortcuts, host);
        _title = group.Title;
        group.Provider.Changed += Refresh;
        shortcuts.Changed += OnShortcutsChanged;
        Refresh();
    }

    private IPresetProvider Provider => _group.Provider;

    public string Id => _group.Id;
    public string Section => _group.Section;

    /// The group's name; while the window searches every section, its page's name comes first.
    [ObservableProperty] private string _title;

    /// The card the window was opened at (outlined).
    [ObservableProperty] private bool _isTarget;

    public ObservableCollection<PresetRowViewModel> Rows { get; } = new();

    public bool IsEmpty => Rows.Count == 0;
    public bool IsFiltering => _filter.Length > 0;

    public string AddText => Provider.Words.AddText;
    public string AddTip => Provider.AddUnavailableReason ?? Provider.Words.AddTip;
    public bool CanAdd => Provider.AddUnavailableReason is null;

    public bool HasSwitch => Provider.Switch is not null;
    public string SwitchLabel => Provider.Switch?.Label ?? string.Empty;
    public string SwitchTip => Provider.Switch?.Tip ?? string.Empty;

    public bool SwitchOn
    {
        get => Provider.Switch?.Get() ?? false;
        set
        {
            if (Provider.Switch is not { } choice || choice.Get() == value) return;
            choice.Set(value);
            OnPropertyChanged();
        }
    }

    public bool CanImport => Provider.CanExchangeFiles;

    /// The search text is in the group's own words (its page, name, apply and add words, tooltip, switch): all its presets show.
    private bool GroupMatches => TextMatch.Any(_filter, _group.Words.ToArray());

    /// Only the rows whose name or shortcut contains the text (<see cref="TextMatch"/>), or all of them when the group's own words do; empty = all.
    internal void Filter(string text, bool showSection)
    {
        _filter = text.Trim();
        Title = showSection ? Heading.Join(_group.Section, _group.Title) : _group.Title;
        Refresh();
    }

    /// Shows the group's presets as they are now. Rows are kept by id, so a name being typed survives a change elsewhere.
    public void Refresh()
    {
        var entries = Provider.Entries
            .Select(e => (Entry: e, Shortcut: _shortcuts.ShortcutOf(_group, e.Id)))
            .Where(x => !IsFiltering || GroupMatches || TextMatch.Any(_filter, x.Entry.Name, x.Shortcut))
            .ToList();
        var kept = Rows.ToDictionary(r => r.Id);
        var rows = entries.Select(x =>
        {
            if (!kept.TryGetValue(x.Entry.Id, out var row))
                return new PresetRowViewModel(x.Entry, x.Shortcut, Provider.Words.ApplyText, Provider.CanDuplicate, Provider.CanExchangeFiles, this);
            row.Update(x.Entry, x.Shortcut);
            return row;
        }).ToList();
        ListDiff.Apply(Rows, rows, r => r);
        foreach (var name in new[] { nameof(IsEmpty), nameof(IsFiltering), nameof(AddTip), nameof(CanAdd), nameof(SwitchOn) }) OnPropertyChanged(name);
    }

    private void OnShortcutsChanged(string groupId)
    {
        if (groupId == _group.Id) Refresh();
    }

    // ---------------------------------------------------------------- the card's own actions

    [RelayCommand]
    private void Add()
    {
        var change = Provider.Add();
        _host.Say(change.Problem);
        if (change.Id is not { } id) return;
        Refresh();
        Rows.FirstOrDefault(r => r.Id == id)?.BeginRenameCommand.Execute(null);
    }

    [RelayCommand]
    private void Import() => _host.ImportRequested(this);

    /// A file's bytes, read by the window: the preset it holds is added.
    public void ImportFile(string fileName, byte[] bytes) => _host.Say(Provider.Import(fileName, bytes).Problem);

    /// A row was dragged (<see cref="Shared.Ui.Controls.ListDragReorder"/>): it goes in front of the <paramref name="at"/>-th of the rows that stay.
    public void MoveRows(IReadOnlyList<int> rows, int at)
    {
        if (rows is not [var from] || from >= Rows.Count || IsFiltering) return;
        MoveTo(Rows[from], at);
    }

    /// Alt+Up / Alt+Down on a row.
    public void Move(PresetRowViewModel row, int delta)
    {
        var at = Rows.IndexOf(row) + delta;
        if (IsFiltering || at < 0 || at >= Rows.Count) return;
        MoveTo(row, at);
    }

    private void MoveTo(PresetRowViewModel row, int at)
    {
        if (!row.CanMove)
        {
            _host.Say(row.Entry.BuiltIn ? "Built-in presets keep their place." : Provider.Move(row.Id, at).Problem);
            Refresh();
            return;
        }
        _host.Say(Provider.Move(row.Id, at).Problem);
        Refresh();
    }

    // ---------------------------------------------------------------- what a row asks

    void IPresetRowOwner.Apply(PresetRowViewModel row)
    {
        _host.Say(Provider.Apply(row.Id));
        Refresh(); // the preset in use moved
    }

    void IPresetRowOwner.Rename(PresetRowViewModel row, string name)
    {
        var change = Provider.Rename(row.Id, name);
        _host.Say(change.Problem);
        if (change.Problem is null) row.IsEditing = false;
    }

    void IPresetRowOwner.RenameStarted(PresetRowViewModel row) => _host.RenameStarted(row);

    void IPresetRowOwner.Delete(PresetRowViewModel row)
    {
        var name = row.Name;
        var clears = row.Entry.ClearsOnly;
        if (Provider.Delete(row.Id) is not { } undo) return;
        _host.Say(null);
        _host.OfferUndo(clears ? $"Cleared \"{name}\"" : $"Deleted \"{name}\"", undo);
    }

    void IPresetRowOwner.Duplicate(PresetRowViewModel row) => _host.Say(Provider.Duplicate(row.Id).Problem);

    void IPresetRowOwner.Export(PresetRowViewModel row) => _host.ExportRequested(this, row);

    /// The bytes of a row's export file (null when it cannot be written).
    public byte[]? ExportOf(PresetRowViewModel row) => Provider.Export(row.Id);

    void IPresetRowOwner.SetShortcut(PresetRowViewModel row, string gesture)
    {
        var problem = _shortcuts.Set(_group, row.Entry, gesture);
        _host.Say(problem);
        row.ShowShortcut(_shortcuts.ShortcutOf(_group, row.Id)); // a refused key: the box shows the shortcut it really has
    }

    public void Dispose()
    {
        _group.Provider.Changed -= Refresh;
        _shortcuts.Changed -= OnShortcutsChanged;
    }
}
