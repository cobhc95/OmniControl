using System.Collections.ObjectModel;
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

/// <summary>One part of the picker: its title ("Suggested for this", "Your icons", "All icons", then one part per category) and its cells; hidden when empty.</summary>
public sealed class IconSection
{
    public IconSection(string title, IReadOnlyList<IconCell> cells) => (Title, Cells) = (title, cells);

    public string Title { get; }
    public IReadOnlyList<IconCell> Cells { get; }
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

    public IconPickerModel(IconStore store, IconRequest request)
    {
        (_store, _request) = (store, request);
        _selectedValue = IconLibrary.Current(request.Current);
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

    private void Rebuild()
    {
        Sections.Clear();
        var searching = Search.Trim().Length > 0;
        if (!searching) Add("Suggested for this", _request.Suggested.Select(Library));
        Add("Your icons", _store.Mine.Where(i => !searching || TextMatch.Any(Search, i.Name)).Select(i => new IconCell(i.Value, i.Name, IconResources.Find(i.Value), true)));
        var all = IconLibrary.Search(Search);
        // One part per category of the matches, in the library's order, each under its own heading with its count.
        foreach (var part in all.GroupBy(i => i.Category))
            Add($"{part.Key} · {part.Count()}", part.Select(i => Library(i.Id)));
        SelectValue(_selectedValue);
    }

    private void Add(string title, IEnumerable<IconCell?> cells)
    {
        var list = cells.OfType<IconCell>().ToList();
        if (list.Count > 0) Sections.Add(new IconSection(title, list));
    }

    private static IconCell? Library(string id) =>
        IconLibrary.Find(id) is { } icon ? new IconCell(icon.Id, icon.Name, IconResources.Find(icon.Id), false) : null;
}
