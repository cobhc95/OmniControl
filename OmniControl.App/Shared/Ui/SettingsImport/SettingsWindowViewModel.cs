using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Shared.Ui.SettingsImport;

/// Which step the settings window is on.
public enum ImportPhase
{
    Loading,
    Choosing,
    Running,
    Done,
}

/// The one settings window: "Export settings", "Import settings from a file" and "Import settings from a PC". It asks the source what it has,
/// lets the person tick areas (grouped like the sidebar), then exports them, or imports them area by area with its progress and shows the
/// result: what was checked and what failed and was kept as it was. The import's work is <see cref="SettingsImporter"/>'s; this holds the
/// steps and what the window shows. Applying stays on the UI thread (owners reload live there); only reading the source and the backup are awaited.
public sealed partial class SettingsWindowViewModel : ObservableObject, IDisposable
{
    /// A long result shows this many checked areas, then "+ N more checked".
    private const int ShownChecked = 4;

    private const string BackupLabel = "Backup of this PC";

    private readonly ISettingsSource _source;
    private readonly SettingsImporter? _importer;
    private readonly Func<IReadOnlyList<ImportArea>, Task<string?>>? _export;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<ImportProgressRow> _rows = new();
    private bool _disposed;

    /// Imports from <paramref name="source"/>, reloading each store through its owner's <paramref name="hooks"/>.
    public SettingsWindowViewModel(IImportSource source, ImportHooks hooks) : this(source) => _importer = new SettingsImporter(hooks);

    /// Exports the areas of <paramref name="source"/> (what is saved here) through <paramref name="export"/>, which gives the result sentence (null: cancelled).
    public SettingsWindowViewModel(ISettingsSource source, Func<IReadOnlyList<ImportArea>, Task<string?>> export) : this(source) => _export = export;

    private SettingsWindowViewModel(ISettingsSource source)
    {
        _source = source;
        _ = Guard.RunAsync("Settings window: reading the areas", LoadAsync);
    }

    public bool IsExport => _export is not null;

    public bool IsImport => !IsExport;

    public ObservableCollection<ImportGroupItem> Groups { get; } = new();

    public ObservableCollection<ImportProgressRow> Progress { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsChoosing), nameof(IsRunning), nameof(IsDone), nameof(IsAsking), nameof(ShowsProgress), nameof(Title), nameof(CanCopy))]
    private ImportPhase _phase = ImportPhase.Loading;

    /// Why the source's areas could not be read (it went away, it did not answer, a bad file), one plain sentence; empty: none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem), nameof(IsLoading))] private string _problem = string.Empty;

    /// One plain line under the list (exported, undone, could not undo).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _status = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private int _importedCount;

    public bool IsLoading => Phase == ImportPhase.Loading && !HasProblem;

    public bool IsChoosing => Phase == ImportPhase.Choosing;

    public bool IsRunning => Phase == ImportPhase.Running;

    public bool IsDone => Phase == ImportPhase.Done;

    /// The step before the work runs: the foot offers Cancel (and the action once the areas are known).
    public bool IsAsking => Phase is ImportPhase.Loading or ImportPhase.Choosing;

    /// The progress and result list shows while an import runs and after.
    public bool ShowsProgress => IsImport && Phase is ImportPhase.Running or ImportPhase.Done;

    /// "Copy details" once an import has a result to copy.
    public bool CanCopy => IsImport && IsDone;

    /// The note under the list says what an import does.
    public bool ShowsImportNote => IsImport && IsChoosing;

    public bool HasProblem => Problem.Length > 0;

    public bool HasStatus => Status.Length > 0;

    public string Title => (IsExport, Phase) switch
    {
        (true, _) => "Export settings",
        (_, ImportPhase.Running) => $"Importing from {_source.Name}…",
        (_, ImportPhase.Done) => $"Imported {ImportedCount} of {AreaRows.Count} {Areas(AreaRows.Count)}",
        _ => $"Import settings from {_source.Name}",
    };

    private List<ImportProgressRow> AreaRows => _rows.Where(r => r.Label != BackupLabel).ToList();

    private IEnumerable<ImportAreaItem> Items => Groups.SelectMany(g => g.Areas);

    private int Total => Items.Count();

    private int Chosen => Items.Count(i => i.IsChecked);

    /// Every area is ticked.
    public bool AllChecked => Total > 0 && Chosen == Total;

    /// Some, not all, are ticked: the "Everything" tick shows a dash.
    public bool IsPartial => Chosen > 0 && Chosen < Total;

    public string EverythingCount => $"{Chosen} of {Total} areas";

    public string ActionText => IsExport ? $"Export {Chosen} {Areas(Chosen)}…" : $"Import {Chosen} {Areas(Chosen)}";

    public bool CanRun => IsChoosing && Chosen > 0;

    /// The import can still be put back (an area was applied and nothing has been undone).
    [ObservableProperty] private bool _canUndo;

    private static string Areas(int n) => n == 1 ? "area" : "areas";

    private async Task LoadAsync()
    {
        var (offers, why) = await _source.OffersAsync();
        if (_disposed) return;
        if (offers is null)
        {
            Problem = why ?? $"{_source.Name} sent something OmniControl could not read.";
            return;
        }
        foreach (var group in ImportGroups.All)
        {
            var areas = _source.Areas.Where(a => a.Group == group)
                .Select(a => (Area: a, Offer: offers.FirstOrDefault(o => o.Area == a.Name)))
                .Where(x => x.Offer is not null)
                .Select(x => new ImportAreaItem(x.Area, x.Offer!.Count, OnSelectionChanged)).ToList();
            if (areas.Count > 0) Groups.Add(new ImportGroupItem(group.ToUpperInvariant(), areas));
        }
        if (Groups.Count == 0)
        {
            Problem = IsExport ? "Nothing is saved yet." : $"{_source.Name} has no settings saved.";
            return;
        }
        Phase = ImportPhase.Choosing;
        OnSelectionChanged();
    }

    partial void OnPhaseChanged(ImportPhase value)
    {
        OnPropertyChanged(nameof(ShowsImportNote));
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(AllChecked));
        OnPropertyChanged(nameof(IsPartial));
        OnPropertyChanged(nameof(EverythingCount));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleEverything()
    {
        var on = !AllChecked;
        foreach (var item in Items) item.IsChecked = on;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var chosen = Items.Where(i => i.IsChecked).Select(i => i.Area).ToList();
        if (_export is not null)
        {
            if (await _export(chosen) is not { } done) return; // the save dialog was cancelled: still choosing
            Status = done;
            Phase = ImportPhase.Done;
            return;
        }
        await ImportAsync(chosen);
    }

    private async Task ImportAsync(List<ImportArea> chosen)
    {
        _rows.Clear();
        Progress.Clear();
        var backup = new ImportProgressRow(BackupLabel, ImportRowState.Running, "saving");
        _rows.Add(backup);
        _rows.AddRange(chosen.Select(a => new ImportProgressRow(a.Name)));
        foreach (var row in _rows) Progress.Add(row);
        Phase = ImportPhase.Running;

        var problem = await Task.Run(_importer!.MakeBackup);
        if (problem is not null)
        {
            backup.State = ImportRowState.Failed;
            backup.Text = "not saved";
            backup.Why = problem;
            Status = problem;
            foreach (var row in AreaRows) Fail(row, "Not imported.");
            Finish();
            return;
        }
        backup.State = ImportRowState.Checked;
        backup.Text = "saved";

        foreach (var (area, row) in chosen.Zip(AreaRows))
        {
            if (_stop.IsCancellationRequested || _disposed)
            {
                Fail(row, "Stopped before this area. Yours is kept.");
                continue;
            }
            row.State = ImportRowState.Running;
            row.Text = "importing";
            var (files, why) = await ((IImportSource)_source).AreaAsync(area.Name);
            Show(row, files is null ? new AreaResult(area.Name, AreaOutcome.Failed, Why: why ?? "It did not send this area.") : _importer.Apply(area, files));
        }
        Finish();
    }

    private static void Show(ImportProgressRow row, AreaResult result)
    {
        row.Fact = result.Fact ?? string.Empty;
        switch (result.Outcome)
        {
            case AreaOutcome.Checked:
                row.State = ImportRowState.Checked;
                row.Text = "checked";
                break;
            default:
                Fail(row, result.Why ?? "Not applied.");
                break;
        }
    }

    private static void Fail(ImportProgressRow row, string why)
    {
        row.State = ImportRowState.Failed;
        row.Text = "not applied, kept yours";
        row.Why = why;
    }

    private void Finish()
    {
        ImportedCount = AreaRows.Count(r => r.State == ImportRowState.Checked);
        CanUndo = _importer!.HasApplied;
        Phase = ImportPhase.Done;
        Collapse();
    }

    /// A long result keeps every row that has something to say and shows only the first few plain "checked" ones, then "+ N more checked".
    private void Collapse()
    {
        var hidden = AreaRows.Where(r => r.IsChecked && !r.HasFact).Skip(ShownChecked).ToList();
        if (hidden.Count < 2) return;
        Progress.Clear();
        Progress.Add(_rows[0]);
        foreach (var row in AreaRows.Where(r => !hidden.Contains(r))) Progress.Add(row);
        Progress.Add(new ImportProgressRow($"+ {hidden.Count} more", ImportRowState.More, "checked"));
    }

    [RelayCommand]
    private void Stop() => _stop.Cancel();

    [RelayCommand]
    private void Undo()
    {
        var problem = _importer!.Undo();
        CanUndo = _importer.HasApplied;
        Status = problem ?? "Put back from the backup of this PC.";
        if (problem is not null) return;
        foreach (var row in AreaRows.Where(r => r.State == ImportRowState.Checked))
        {
            row.State = ImportRowState.Pending;
            row.Text = "put back";
            row.Fact = string.Empty;
        }
    }

    /// What "Copy details" copies: the title and one line per row.
    public string Details => string.Join(Environment.NewLine, new[] { Title }.Concat(_rows.Select(r =>
        $"{r.Label}: {r.Text}{(r.HasFact ? $" ({r.Fact})" : string.Empty)}{(r.Why.Length > 0 ? $" - {r.Why}" : string.Empty)}")));

    public void Dispose()
    {
        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
    }
}
