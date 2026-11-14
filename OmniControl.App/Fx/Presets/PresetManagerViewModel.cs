using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Services;

namespace OmniControl.App.Fx.Presets;

/// <summary>
/// The preset manager (ported from TabForge's preset manager window): the effect kinds with their preset counts at the left, the presets
/// of the chosen kind at the right (built-in ones greyed and tagged), a search across every kind, and the actions: save the settings of the
/// effect it was opened from, rename, duplicate, move, delete (with a few seconds of Undo), import and export (PresetManagerViewModel.Actions.cs).
/// The window holds only layout and the file and name dialogs; the changes go through <see cref="UserPresetStore"/>.
/// </summary>
public sealed partial class PresetManagerViewModel : ObservableObject, IDisposable
{
    private readonly UserPresetStore _store;
    private readonly PresetManagerRequest _request;
    private readonly DispatcherTimer _undoTimer = new() { Interval = Timing.PresetUndo };
    private IReadOnlyList<PresetRowItem> _selected = Array.Empty<PresetRowItem>();
    private Action? _undo;
    private bool _filling;

    public PresetManagerViewModel(UserPresetStore store, PresetManagerRequest request)
    {
        _store = store;
        _request = request;
        foreach (var kind in PresetCatalog.Kinds) Kinds.Add(new PresetKindItem(kind));
        _node = Kinds.FirstOrDefault(k => k.Kind.Kind == request.Kind.Kind) ?? Kinds[0];
        _undoTimer.Tick += (_, _) => ClearUndo();
        store.Changed += Refill;
        Fill(null);
    }

    public ObservableCollection<PresetKindItem> Kinds { get; } = new();
    public ObservableCollection<PresetRowItem> Rows { get; } = new();

    /// <summary>The kind whose presets show (ignored while a search lists several).</summary>
    [ObservableProperty] private PresetKindItem _node;

    /// <summary>Part of a preset's name, searched in every kind.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSearching))] private string _search = string.Empty;

    [ObservableProperty] private string _heading = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string _message = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUndo))] private string _undoText = string.Empty;

    /// <summary>Asks a yes / no question (replacing a preset of the same name); the window supplies the real box.</summary>
    public Func<string, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(false);

    /// <summary>The window selects exactly these rows (after a change, so the user sees where the preset went).</summary>
    public event Action<IReadOnlyList<PresetRowItem>>? SelectRequested;

    /// <summary>A row was put into rename: the window gives its box the keyboard.</summary>
    public event Action<PresetRowItem>? RenameStarted;

    public bool IsSearching => Search.Trim().Length > 0;
    public bool HasMessage => Message.Length > 0;
    public bool HasUndo => UndoText.Length > 0;
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyText => IsSearching ? "No preset matches." : "No presets of your own yet. Save current keeps this effect's settings.";

    /// <summary>The effect the manager was opened from (on its Save current button's tooltip).</summary>
    public string SourceName => _request.Kind.Name;

    // ---- what the toolbar can do with the selection ----

    public bool CanSaveCurrent => !IsSearching && Node.Kind.Kind == _request.Kind.Kind;
    public bool CanRename => _selected is [{ BuiltIn: false }];
    public bool CanDuplicate => _selected.Count > 0;
    public bool CanDelete => _selected.Any(r => !r.BuiltIn);
    public bool CanMoveUp => !IsSearching && CanMove(-1);
    public bool CanMoveDown => !IsSearching && CanMove(1);
    public bool CanExport => _selected.Count == 1;
    public bool CanImport => !IsSearching;
    public bool CanShowInList => _selected.Count == 1 && IsSearching;

    /// <summary>The window passes the rows that are selected now.</summary>
    public void SetSelected(IEnumerable<PresetRowItem> rows)
    {
        _selected = rows.ToList();
        foreach (var row in Rows.Where(r => r.IsEditing && !_selected.Contains(r))) row.IsEditing = false;
        ToolsChanged();
    }

    partial void OnNodeChanged(PresetKindItem value)
    {
        if (_filling) return;
        if (IsSearching) Search = string.Empty;
        else Fill(null);
    }

    partial void OnSearchChanged(string value) => Fill(null);

    private void ToolsChanged()
    {
        foreach (var name in new[] { nameof(CanSaveCurrent), nameof(CanRename), nameof(CanDuplicate), nameof(CanDelete), nameof(CanMoveUp),
                     nameof(CanMoveDown), nameof(CanExport), nameof(CanImport), nameof(CanShowInList) })
            OnPropertyChanged(name);
    }

    private bool CanMove(int delta) => _selected.Where(r => !r.BuiltIn).GroupBy(r => r.Kind.Kind).Any(g =>
        PresetRules.BlockSteps(_store.Names(g.Key), g.Select(r => r.Name).ToList(), delta).Count > 0);

    // ---- the lists ----

    private void Refill() => Fill(null);

    private void Fill(IReadOnlyList<PresetRowItem>? select)
    {
        var keep = select ?? _selected;
        _filling = true;
        try
        {
            foreach (var item in Kinds) item.Count = EntriesOf(item.Kind).Count;
            var query = Search.Trim();
            var entries = query.Length > 0
                ? PresetCatalog.Kinds.SelectMany(k => EntriesOf(k, true)).Where(r => TextMatch.Any(query, r.Name)).ToList()
                : EntriesOf(Node.Kind);
            Rows.Clear();
            foreach (var row in entries) Rows.Add(row);
            Heading = query.Length > 0 ? $"{entries.Count} {(entries.Count == 1 ? "preset" : "presets")} found" : Node.Title;
        }
        finally { _filling = false; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        var again = Rows.Where(r => keep.Any(k => r.Is(k.Kind, k.Name))).ToList();
        _selected = again;
        SelectRequested?.Invoke(again.Count > 0 || Rows.Count == 0 ? again : new[] { Rows[0] });
        ToolsChanged();
    }

    /// <summary>The kind's presets as rows: the built-in ones, then the user's own in their order.</summary>
    private List<PresetRowItem> EntriesOf(FxKind kind, bool showWhere = false) =>
        PresetCatalog.BuiltIns(kind).Select(b => new PresetRowItem(kind, b.Name, b.Content, true, null) { ShowWhere = showWhere })
            .Concat(_store.Of(kind.Kind).Select(p => new PresetRowItem(kind, p.Name, p.Content, false, p.Saved) { ShowWhere = showWhere })).ToList();

    /// <summary>Leaves the search and shows the one selected hit in its own kind.</summary>
    [RelayCommand]
    private void ShowInList()
    {
        if (_selected is not [{ } row]) return;
        Search = string.Empty;
        Node = Kinds.First(k => k.Kind.Kind == row.Kind.Kind);
        Fill(new[] { row });
    }

    public void Dispose()
    {
        _store.Changed -= Refill;
        _undoTimer.Stop();
    }

    private void Say(string? text) => Message = text ?? string.Empty;
}
