using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Services;

namespace OmniControl.App.Icons;

/// <summary>What the icon picker is asked: whose icon it is (the title), the value it has now (selected; null: its default) and the suggestions.</summary>
public sealed record IconRequest(string Name, string? Current, IReadOnlyList<string> Suggested);

/// <summary>The picker's answer: the icon value chosen, or null for "Default" (the item shows its own default icon again).</summary>
public sealed record IconPick(string? Value);

/// <summary>One cell of the picker's grids: an icon value, its name (the tooltip; a user icon's can be edited) and its drawing.</summary>
public sealed partial class IconCell : ObservableObject
{
    public IconCell(string value, string name, IconImage? image, bool isMine)
    {
        (Value, Name, Image, IsMine) = (value, name, image, isMine);
    }

    public string Value { get; }
    public string Name { get; }
    public IconImage? Image { get; }
    public bool IsMine { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>One part of the picker: its title ("Suggested for this", "Your icons", then one part per category) and its cells; kept while the search changes, so the cells that stay are not rebuilt.</summary>
public sealed partial class IconSection : ObservableObject
{
    public IconSection(string key, string title) => (Key, _title) = (key, title);

    public string Key { get; }
    public ObservableCollection<IconCell> Cells { get; } = new();

    [ObservableProperty] private string _title;
}

/// <summary>
/// The icon picker (docs/mockups/icon-library.html): a search over names and keywords, "Suggested for this", "Your icons" (when there are
/// any) and every library icon, 8 per row. The arrows move the selection over all the cells as one grid per part; Enter uses it. The user
/// icon that is selected can be renamed or removed. Its answer is <see cref="Answer"/>.
/// </summary>
public sealed partial class IconPickerModel : ObservableObject
{
    /// <summary>Cells per row in every part of the picker.</summary>
    public const int Columns = 8;

    private readonly IconStore _store;
    private readonly IconRequest _request;
    private IconCell? _selected;
    private readonly SearchRunner<IReadOnlyList<SectionSpec>> _runner;
    private readonly Dictionary<(string Section, string Value), IconCell> _libraryCells = new();
    private int _generation;

    /// <summary>One part of a search result: plain data made off the UI thread, turned into cells on it.</summary>
    private sealed record SectionSpec(string Key, string Title, IReadOnlyList<CellSpec> Cells);

    private sealed record CellSpec(string Value, string Name, bool IsMine);

    public IconPickerModel(IconStore store, IconRequest request)
    {
        (_store, _request) = (store, request);
        _selectedValue = IconLibrary.Current(request.Current);
        _runner = new SearchRunner<IReadOnlyList<SectionSpec>>(Show, "icons");
        Rebuild();
    }

    public string Title => $"Icon for: {_request.Name}";

    public ObservableCollection<IconSection> Sections { get; } = new();

    [ObservableProperty] private string _search = string.Empty;

    private string? _selectedValue;

    /// <summary>The selected icon is one of the user's: its name box and Remove show.</summary>
    public bool SelectedIsMine => _selected?.IsMine == true;

    /// <summary>The selected user icon's name, editable; empty for a library icon.</summary>
    public string SelectedName
    {
        get => SelectedIsMine ? _store.Mine.FirstOrDefault(i => i.Value == _selected!.Value)?.Name ?? string.Empty : string.Empty;
        set
        {
            if (_selected is { IsMine: true } cell) _store.Rename(cell.Value, value);
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>What the window returns when it closes with "Use" or "Default"; null when it was cancelled.</summary>
    public IconPick? Answer { get; private set; }

    partial void OnSearchChanged(string value) => Rebuild();

    /// <summary>Stops a search still running (the window closes).</summary>
    public void Close() => _runner.Dispose();

    /// <summary>Selects a cell (a click).</summary>
    public void Select(IconCell cell) => SelectValue(cell.Value);

    /// <summary>Use the selected icon.</summary>
    public bool Use()
    {
        if (_selected is null) return false;
        Answer = new IconPick(_selected.Value);
        return true;
    }

    /// <summary>Back to the item's own default icon.</summary>
    public void UseDefault() => Answer = new IconPick(null);

    /// <summary>"+ Add your own…" and "Own image…": the file is added to Your icons and selected. Null when it worked, else one plain sentence.</summary>
    public string? Add(string path)
    {
        var (value, problem) = _store.Import(path);
        if (value is null) return problem;
        Search = string.Empty;
        _selectedValue = value;
        Rebuild();
        return null;
    }

    /// <summary>Removes the selected user icon.</summary>
    public void RemoveSelected()
    {
        if (_selected is not { IsMine: true } cell) return;
        _store.Remove(cell.Value);
        _selectedValue = null;
        Rebuild();
    }

    /// <summary>The arrows: Left/Right one cell (on into the next part), Up/Down one row (into the part above or below at the same column).</summary>
    public void Move(int dx, int dy)
    {
        var parts = Sections.Select(s => s.Cells.ToList()).Where(c => c.Count > 0).ToList();
        if (parts.Count == 0) return;
        var (part, at) = Locate(parts);
        if (part < 0) { SelectValue(parts[0][0].Value); return; }
        if (dx != 0)
        {
            var flat = parts.SelectMany(c => c).ToList();
            var index = flat.IndexOf(parts[part][at]) + dx;
            SelectValue(flat[Math.Clamp(index, 0, flat.Count - 1)].Value);
            return;
        }
        var target = at + dy * Columns;
        if (target >= 0 && target < parts[part].Count) { SelectValue(parts[part][target].Value); return; }
        var column = at % Columns;
        if (dy > 0 && target >= parts[part].Count && at / Columns < (parts[part].Count - 1) / Columns) { SelectValue(parts[part][^1].Value); return; }
        var next = part + Math.Sign(dy);
        if (next < 0 || next >= parts.Count) return;
        var cells = parts[next];
        var row = dy > 0 ? 0 : (cells.Count - 1) / Columns;
        SelectValue(cells[Math.Min(row * Columns + column, cells.Count - 1)].Value);
    }

    private (int Part, int At) Locate(IReadOnlyList<List<IconCell>> parts)
    {
        for (var p = 0; p < parts.Count; p++)
        {
            var i = _selected is null ? -1 : parts[p].IndexOf(_selected);
            if (i >= 0) return (p, i);
        }
        return (-1, -1);
    }

    private void SelectValue(string? value)
    {
        _selectedValue = value;
        // The first cell with the value (a suggestion may also be in All icons): that one is the selection the arrows move from.
        _selected = Sections.SelectMany(s => s.Cells).FirstOrDefault(c => c.Value == value);
        foreach (var cell in Sections.SelectMany(s => s.Cells)) cell.IsSelected = cell == _selected;
        OnPropertyChanged(nameof(SelectedIsMine));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Starts the search for the typed words: the matching runs off the UI thread (an older one is cancelled), the first letter at once, later keys after a short wait.</summary>
    private void Rebuild()
    {
        var query = Search;
        var suggested = _request.Suggested.Select(IconLibrary.Find).OfType<LibraryIcon>().Select(i => new CellSpec(i.Id, i.Name, false)).ToList();
        var mine = _store.Mine.Select(i => (i.Value, i.Name)).ToList();
        if (query.Trim().Length == 0)
        {
            _runner.Cancel();
            Show(Compute(query, suggested, mine, CancellationToken.None));
            return;
        }
        _runner.Request(cancel => Compute(query, suggested, mine, cancel));
    }

    // Only reads data that does not change (the library, its search index and copies taken on the UI thread).
    private static IReadOnlyList<SectionSpec> Compute(string query, IReadOnlyList<CellSpec> suggested, IReadOnlyList<(string Value, string Name)> mine, CancellationToken cancel)
    {
        var searching = query.Trim().Length > 0;
        var parts = new List<SectionSpec>();
        if (!searching && suggested.Count > 0) parts.Add(new SectionSpec("suggested", "Suggested for this", suggested));
        var own = mine.Where(i => !searching || TextMatch.Any(query, i.Name)).Select(i => new CellSpec(i.Value, i.Name, true)).ToList();
        if (own.Count > 0) parts.Add(new SectionSpec("mine", "Your icons", own));
        // One part per category of the matches, in the library's order, each under its own heading with its count.
        foreach (var part in IconLibrary.Search(query, cancel).GroupBy(i => i.Category))
            parts.Add(new SectionSpec("category:" + part.Key, $"{part.Key} · {part.Count()}", part.Select(i => new CellSpec(i.Id, i.Name, false)).ToList()));
        return parts;
    }

    /// <summary>Shows a result: the first screenful of cells at once, the rest in the next frames (a newer result drops the rest of an older one).</summary>
    private void Show(IReadOnlyList<SectionSpec> parts)
    {
        var generation = ++_generation;
        Apply(parts, Timing.IconPickerFirstCells);
        if (parts.Sum(p => p.Cells.Count) <= Timing.IconPickerFirstCells) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _generation) Apply(parts, int.MaxValue);
        }, DispatcherPriority.Background);
    }

    private void Apply(IReadOnlyList<SectionSpec> parts, int budget)
    {
        var wanted = new List<IconSection>();
        foreach (var part in parts)
        {
            if (budget <= 0) break;
            var section = Sections.FirstOrDefault(s => s.Key == part.Key) ?? new IconSection(part.Key, part.Title);
            section.Title = part.Title;
            var cells = part.Cells.Take(budget).Select(c => CellFor(part.Key, c)).ToList();
            budget -= cells.Count;
            ListDiff.Apply(section.Cells, cells, c => c.Value, (a, b) => a.Value == b.Value && a.Name == b.Name && a.IsMine == b.IsMine);
            wanted.Add(section);
        }
        ListDiff.Apply(Sections, wanted, s => s.Key);
        SelectValue(_selectedValue);
    }

    // A library cell is made once per part and kept; a user icon's is made again (its name can change).
    private IconCell CellFor(string section, CellSpec spec)
    {
        if (spec.IsMine) return new IconCell(spec.Value, spec.Name, IconResources.Find(spec.Value), true);
        if (!_libraryCells.TryGetValue((section, spec.Value), out var cell))
            _libraryCells[(section, spec.Value)] = cell = new IconCell(spec.Value, spec.Name, IconResources.Find(spec.Value), false);
        return cell;
    }
}
