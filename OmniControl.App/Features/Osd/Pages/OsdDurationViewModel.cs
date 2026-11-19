using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Osd.Pages;

/// The "OSD duration" window: "Everything else" (the same setting as the page's Duration card) and the
/// exceptions per app or event, first match wins. Every change goes to <see cref="OsdDurations"/> at once (no Apply); the rows follow
/// the settings when they change elsewhere (the card, an import).
public sealed partial class OsdDurationViewModel : ObservableObject, IDisposable
{
    private readonly OsdService _osd;
    private readonly Func<IReadOnlyList<RunningProgram>> _listedApps;
    private IReadOnlyList<RunningProgram> _running = Array.Empty<RunningProgram>();
    private bool _loading;

    public ObservableCollection<OsdDurationRowItem> Rows { get; } = new();

    /// "Everything else", in seconds.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EverythingElseText))]
    private double _everythingElse;

    public string EverythingElseText => OsdDurationRowItem.Words(EverythingElse);

    /// No exceptions yet: the empty-list row shows.
    public bool HasNoRows => Rows.Count == 0;

    /// <param name="listedApps">The programs of the app lists (offered with the programs running now).</param>
    public OsdDurationViewModel(OsdService osd, Func<IReadOnlyList<RunningProgram>> listedApps)
    {
        (_osd, _listedApps) = (osd, listedApps);
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoRows));
        Load();
        _osd.SettingsChanged += Load;
        _ = FindRunningAsync();
    }

    partial void OnEverythingElseChanged(double value)
    {
        if (!_loading) _osd.Durations.SetEverythingElse(value);
    }

    /// "+ add exception": the first event that has none yet, at the "Everything else" time; the chip then picks the app or event.
    [RelayCommand]
    private void AddException()
    {
        var events = _osd.Elements.Select(e => e.Name).ToList();
        var name = events.FirstOrDefault(n => !_osd.Durations.Rules.Any(r => r.Kind == OsdDurationKind.Event && r.Key == n)) ?? events.FirstOrDefault();
        if (name is null) return;
        _osd.Durations.Add(OsdDurationKind.Event, name, name);
    }

    /// A drag moved rows (<see cref="ListDragReorder"/>).
    public void Move(IReadOnlyList<int> rows, int insertAt) => _osd.Durations.Move(rows, insertAt);

    // The settings changed (here, on the card or by an import): the same exceptions only take their new values, so a row being used is kept.
    private void Load()
    {
        _loading = true;
        EverythingElse = _osd.Durations.EverythingElse;
        _loading = false;
        var rules = _osd.Durations.Rules;
        if (rules.Count == Rows.Count && rules.Select((r, i) => ReferenceEquals(r, Rows[i].Rule)).All(same => same))
        {
            foreach (var row in Rows) row.Load();
            return;
        }
        Rows.Clear();
        foreach (var rule in rules) Rows.Add(new OsdDurationRowItem(rule, _osd.Durations, MenuRows));
    }

    private async Task FindRunningAsync()
    {
        await Guard.RunAsync("OSD duration: programs running now", async () => _running = await Task.Run(ProgramIdentity.Running));
    }

    // The chip's menu: the apps (the app lists' and the ones running now, each once, by name), then the OSD's events.
    private IReadOnlyList<MenuRowItem> MenuRows(OsdDurationRule rule)
    {
        var rows = new List<MenuRowItem> { MenuRowItem.Header("Apps") };
        var apps = _listedApps().Concat(_running)
            .Concat(rule.Kind == OsdDurationKind.App ? new[] { new RunningProgram(rule.Key, rule.Name) } : Array.Empty<RunningProgram>())
            .Where(a => a.Id.Length > 0).GroupBy(a => a.Id).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var app in apps)
            rows.Add(Row(app.Name, rule.Kind == OsdDurationKind.App && rule.Key == app.Id, () => _osd.Durations.Retarget(rule, OsdDurationKind.App, app.Id, app.Name)));
        rows.Add(MenuRowItem.Header("Events", ruleAbove: true));
        foreach (var element in _osd.Elements)
            rows.Add(Row(element.Name, rule.Kind == OsdDurationKind.Event && rule.Key == element.Name, () => _osd.Durations.Retarget(rule, OsdDurationKind.Event, element.Name, element.Name)));
        return rows;
    }

    private static MenuRowItem Row(string text, bool on, Action pick) => new(text, string.Empty, on, string.Empty, new RelayCommand(pick));

    public void Dispose() => _osd.SettingsChanged -= Load;
}
