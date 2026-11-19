using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx.Plugins;

/// The Add effect window: a searchable list of built-in effects and plug-ins (every type of <see cref="FxCatalog.PluginTypes"/>, each
/// with its tag), with a scan of the plug-in folders and loading a file by hand.
public sealed partial class AddEffectViewModel : ObservableObject
{
    private const string AllKinds = "All";

    private readonly PluginLibrary _library;
    private readonly bool _allowFolders;
    private CancellationTokenSource? _scan;

    /// <param name="allowFolders">Whether "Device folder" is offered: only the master chain can hold one.</param>
    public AddEffectViewModel(PluginLibrary library, bool allowFolders)
    {
        _library = library;
        _allowFolders = allowFolders;
        Refresh();
        Status = _library.HasScanned ? string.Empty : $"Scan the plug-in folders or load a {Tags} file. {OtherPlaces}";
    }

    /// Where the user is sent for plug-ins installed elsewhere.
    private const string OtherPlaces = "Other places: App settings > Plug-in folders.";

    private static string Tags => string.Join(" or ", FxCatalog.PluginTypes.Select(t => t.Tag));

    public ObservableCollection<PluginEntry> Results { get; } = new();
    public IReadOnlyList<string> Kinds { get; } = new[] { AllKinds, PluginEntry.BuiltIn }.Concat(FxCatalog.PluginTypes.Select(t => t.Tag)).ToList();

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _kind = AllKinds;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAdd))] private PluginEntry? _selected;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ScanText))] private bool _scanning;

    public bool CanAdd => Selected is not null;
    public string ScanText => Scanning ? "Stop scanning" : "Scan plug-in folders";

    /// The file types the Load VST file… picker accepts ("*.vst3", "*.dll").
    public IReadOnlyList<string> FilePatterns { get; } = FxCatalog.PluginTypes.Select(t => "*" + t.Extension).ToList();

    partial void OnQueryChanged(string value) => Refresh();
    partial void OnKindChanged(string value) => Refresh();

    /// The window closes with this when the user adds the selected entry.
    public event Action<PluginEntry>? Chosen;

    public void Add()
    {
        if (Selected is { } entry) Chosen?.Invoke(entry);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (Scanning) { _scan?.Cancel(); return; }
        Scanning = true;
        _scan = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(n => Status = $"Scanning… {n} found");
            await _library.ScanAsync(progress, _scan.Token);
            Status = $"{_library.FoundText()} {OtherPlaces}";
        }
        catch (OperationCanceledException) { Status = "Scan stopped."; }
        finally
        {
            _scan.Dispose();
            _scan = null;
            Scanning = false;
            Refresh();
        }
    }

    /// A plug-in file the user picked by hand: remembers it, shows it and selects it.
    public void LoadFile(string path)
    {
        var entry = _library.AddFile(path, out var problem);
        if (entry is null) { Status = problem ?? string.Empty; return; }
        Query = string.Empty;
        Kind = AllKinds;
        Refresh();
        Selected = Results.FirstOrDefault(r => r.Path == entry.Path);
        Status = $"Loaded {entry.Name}.";
    }

    private void Refresh()
    {
        var keep = Selected;
        var found = _library.All().Where(e => (_allowFolders || !e.IsFolder) && (Kind == AllKinds || e.Format == Kind) && TextMatch.Any(Query, e.Name, e.Format, e.Vendor, e.Description, e.Keywords)).ToList();
        ListDiff.Apply(Results, found, e => e);
        Selected = keep is not null && Results.Contains(keep) ? keep : Results.FirstOrDefault();
    }
}
