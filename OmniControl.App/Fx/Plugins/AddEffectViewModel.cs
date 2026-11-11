using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Fx.Plugins;

/// <summary>The Add effect window: a searchable list of built-in effects and VST3 plug-ins, with a scan of the common plug-in folders and loading a file by hand.</summary>
public sealed partial class AddEffectViewModel : ObservableObject
{
    public const string AllKinds = "All";

    private readonly PluginLibrary _library;
    private CancellationTokenSource? _scan;

    public AddEffectViewModel(PluginLibrary library)
    {
        _library = library;
        Refresh();
        Status = _library.HasScanned ? string.Empty : "Scan the common folders, or load a VST3 file, to add plug-ins to this list.";
    }

    public ObservableCollection<PluginEntry> Results { get; } = new();
    public IReadOnlyList<string> Kinds { get; } = new[] { AllKinds, PluginEntry.BuiltIn, PluginEntry.Vst3 };

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _kind = AllKinds;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAdd))] private PluginEntry? _selected;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ScanText))] private bool _scanning;

    public bool CanAdd => Selected is not null;
    public string ScanText => Scanning ? "Stop scanning" : "Scan common folders";

    partial void OnQueryChanged(string value) => Refresh();
    partial void OnKindChanged(string value) => Refresh();

    /// <summary>The window closes with this when the user adds the selected entry.</summary>
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
            var count = await _library.ScanCommonAsync(progress, _scan.Token);
            Status = count == 0 ? "No VST3 plug-ins found in the common folders. Use Load VST file… for ones installed elsewhere." : $"Found {count} VST3 plug-in{(count == 1 ? "" : "s")}.";
        }
        catch (OperationCanceledException) { Status = "Scan stopped."; }
        finally
        {
            Scanning = false;
            Refresh();
        }
    }

    /// <summary>A plug-in file the user picked by hand: remembers it, shows it and selects it.</summary>
    public void LoadFile(string path)
    {
        var entry = _library.AddFile(path);
        if (entry is null) { Status = "That isn't a VST3 plug-in (a file or folder ending in .vst3)."; return; }
        Query = string.Empty;
        Kind = AllKinds;
        Refresh();
        Selected = Results.FirstOrDefault(r => r.Path == entry.Path);
        Status = $"Loaded {entry.Name}.";
    }

    private void Refresh()
    {
        var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keep = Selected;
        Results.Clear();
        foreach (var e in _library.All())
        {
            if (Kind != AllKinds && e.Format != Kind) continue;
            var haystack = $"{e.Name} {e.Format} {e.Vendor} {e.Description}";
            if (words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase))) Results.Add(e);
        }
        Selected = keep is not null && Results.Contains(keep) ? keep : Results.FirstOrDefault();
    }
}
