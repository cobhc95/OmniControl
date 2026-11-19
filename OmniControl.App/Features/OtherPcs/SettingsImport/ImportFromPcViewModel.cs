using System.Text.Json;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// Which step the import window is on.
public enum ImportPhase
{
    Loading,
    Choosing,
    Running,
    Done,
}

/// "Import settings from a PC": asks the PC what it has, lets the person tick areas (grouped like the
/// sidebar), then runs the import area by area with its progress and shows the result: what was checked and what failed and
/// was kept as it was. The work is <see cref="SettingsImporter"/>'s and the transfer <see cref="ISettingsTransport"/>'s; this holds the steps
/// and what the window shows. Applying stays on the UI thread (owners reload live there); only the network wait and the backup are awaited.
public sealed partial class ImportFromPcViewModel : ObservableObject, IDisposable
{
    /// A long result shows this many checked areas, then "+ N more checked".
    private const int ShownChecked = 4;

    private const string BackupLabel = "Backup of this PC";

    private readonly string _pcId;
    private readonly ISettingsTransport _transport;
    private readonly SettingsImporter _importer;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<ImportProgressRow> _rows = new();
    private bool _disposed;

    public ImportFromPcViewModel(string pcId, ISettingsTransport transport, ImportHooks hooks)
    {
        _pcId = pcId;
        _transport = transport;
        _importer = new SettingsImporter(hooks);
        PcName = transport.NameOf(pcId);
        _ = LoadAsync();
    }

    public string PcName { get; }

    public ObservableCollection<ImportGroupItem> Groups { get; } = new();

    public ObservableCollection<ImportProgressRow> Progress { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsChoosing), nameof(IsRunning), nameof(IsDone), nameof(IsAsking), nameof(ShowsProgress), nameof(Title))]
    private ImportPhase _phase = ImportPhase.Loading;

    /// Why the PC's areas could not be read (it went away, it did not answer), one plain sentence; empty: none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem), nameof(IsLoading))] private string _problem = string.Empty;

    /// One plain line under the list (undone, could not undo).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _status = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private int _importedCount;

    public bool IsLoading => Phase == ImportPhase.Loading && !HasProblem;

    public bool IsChoosing => Phase == ImportPhase.Choosing;

    public bool IsRunning => Phase == ImportPhase.Running;

    public bool IsDone => Phase == ImportPhase.Done;

    /// The step before the import runs: the foot offers Cancel (and Import once the areas are known).
    public bool IsAsking => Phase is ImportPhase.Loading or ImportPhase.Choosing;

    /// The progress and result list shows while the import runs and after.
    public bool ShowsProgress => Phase is ImportPhase.Running or ImportPhase.Done;

    public bool HasProblem => Problem.Length > 0;

    public bool HasStatus => Status.Length > 0;

    public string Title => Phase switch
    {
        ImportPhase.Running => $"Importing from {PcName}…",
        ImportPhase.Done => $"Imported {ImportedCount} of {AreaRows.Count} {(AreaRows.Count == 1 ? "area" : "areas")}",
        _ => $"Import settings from {PcName}",
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

    public string ImportText => $"Import {Chosen} {(Chosen == 1 ? "area" : "areas")}";

    public bool CanImport => IsChoosing && Chosen > 0;

    /// The import can still be put back (an area was applied and nothing has been undone).
    [ObservableProperty] private bool _canUndo;

    private async Task LoadAsync()
    {
        var reply = await _transport.OffersAsync(_pcId);
        if (_disposed) return;
        List<AreaOffer>? offers = null;
        if (reply.Payload is not null)
        {
            try
            {
                offers = JsonSerializer.Deserialize<List<AreaOffer>>(reply.Payload, PcProtocol.Json);
            }
            catch (JsonException)
            {
                // A reply this PC cannot read is reported like any other failure to ask.
            }
        }
        if (offers is null)
        {
            Problem = reply.Why ?? $"{PcName} sent something OmniControl could not read.";
            return;
        }
        foreach (var group in ImportGroups.All)
        {
            var areas = ImportAreas.All.Where(a => a.Group == group)
                .Select(a => (Area: a, Offer: offers.FirstOrDefault(o => o.Area == a.Name)))
                .Where(x => x.Offer is not null)
                .Select(x => new ImportAreaItem(x.Area, x.Offer!.Count, OnSelectionChanged)).ToList();
            if (areas.Count > 0) Groups.Add(new ImportGroupItem(group.ToUpperInvariant(), areas));
        }
        if (Groups.Count == 0)
        {
            Problem = $"{PcName} has no settings saved yet.";
            return;
        }
        Phase = ImportPhase.Choosing;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(AllChecked));
        OnPropertyChanged(nameof(IsPartial));
        OnPropertyChanged(nameof(EverythingCount));
        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(CanImport));
        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleEverything()
    {
        var on = !AllChecked;
        foreach (var item in Items) item.IsChecked = on;
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var chosen = Items.Where(i => i.IsChecked).Select(i => i.Area).ToList();
        _rows.Clear();
        Progress.Clear();
        var backup = new ImportProgressRow(BackupLabel, ImportRowState.Running, "saving");
        _rows.Add(backup);
        _rows.AddRange(chosen.Select(a => new ImportProgressRow(a.Name)));
        foreach (var row in _rows) Progress.Add(row);
        Phase = ImportPhase.Running;

        var problem = await Task.Run(_importer.MakeBackup);
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
            Show(row, await ImportAreaAsync(area));
        }
        Finish();
    }

    private async Task<AreaResult> ImportAreaAsync(ImportArea area)
    {
        var reply = await _transport.AreaAsync(_pcId, area.Name);
        if (reply.Payload is null) return new AreaResult(area.Name, AreaOutcome.Failed, Why: reply.Why ?? "It did not send this area.");
        Dictionary<string, string>? files = null;
        try
        {
            files = JsonSerializer.Deserialize<Dictionary<string, string>>(reply.Payload, PcProtocol.Json);
        }
        catch (JsonException)
        {
            // Reported as the failure of this area just below.
        }
        return files is null
            ? new AreaResult(area.Name, AreaOutcome.Failed, Why: "What arrived could not be read.")
            : _importer.Apply(area, files);
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
        CanUndo = _importer.HasApplied;
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
        var problem = _importer.Undo();
        CanUndo = false;
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
