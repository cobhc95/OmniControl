using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx.Presets;

/// <summary>The manager's changes. Each goes through <see cref="UserPresetStore"/>, which tells the lists to fill again.</summary>
public sealed partial class PresetManagerViewModel
{
    private const string ReservedText = "That name belongs to a built-in preset.";
    private const string ChangeFailed = "The preset could not be changed: ";
    private const string ImportedName = "Imported preset";

    // ---- Save current ----

    /// <summary>Why this text cannot name a preset of the effect the manager was opened from; null when it can.</summary>
    public string? NameError(string text) =>
        PresetRules.Error(text) ?? (BuiltInNames(_request.Kind).Any(n => PresetRules.Same(n, text.Trim())) ? ReservedText : null);

    /// <summary>A preset of the user's own already has this name (saving replaces it).</summary>
    public bool Exists(string text) => _store.Find(_request.Kind.Kind, text.Trim()) is not null;

    /// <summary>Keeps the settings the effect has now as a preset of its kind; an existing one of the name is replaced after a question.</summary>
    public async Task SaveCurrentAsync(string text)
    {
        var name = text.Trim();
        if (NameError(name) is { } error) { Say(error); return; }
        if (Exists(name) && !await Confirm(ReplaceQuestion(name))) return;
        if (!Try(() => _store.Save(_request.Kind.Kind, name, _request.Snapshot()))) return;
        Say(null);
        SelectRequested?.Invoke(Rows.Where(r => r.Is(_request.Kind, name)).ToList());
    }

    private static string ReplaceQuestion(string name) => $"A preset named \"{name}\" already exists. Replace it?";

    // ---- rename ----

    [RelayCommand]
    private void BeginRename()
    {
        if (_selected is not [{ BuiltIn: false } row]) return;
        row.EditText = row.Name;
        row.IsEditing = true;
        RenameStarted?.Invoke(row);
    }

    /// <summary>Applies the typed name; a refused name stays in the box with the reason on the message line.</summary>
    public async Task CommitRenameAsync(PresetRowItem row)
    {
        var name = row.EditText.Trim();
        if (PresetRules.Error(name) is { } error) { Say(error); return; }
        if (BuiltInNames(row.Kind).Any(n => PresetRules.Same(n, name))) { Say(ReservedText); return; }
        if (name == row.Name) { row.IsEditing = false; return; }
        var clash = _store.Names(row.Kind.Kind).FirstOrDefault(n => PresetRules.Same(n, name) && !PresetRules.Same(n, row.Name));
        if (clash is not null && !await Confirm(ReplaceQuestion(clash))) return;
        row.IsEditing = false;
        if (Try(() => _store.Rename(row.Kind.Kind, row.Name, name, clash is not null))) Say(null);
        SelectRequested?.Invoke(Rows.Where(r => r.Is(row.Kind, name)).ToList());
    }

    public void CancelRename(PresetRowItem row) => row.IsEditing = false;

    // ---- duplicate, delete, move ----

    /// <summary>Copies each selected preset (a built-in one too: its copy is the user's own to change).</summary>
    [RelayCommand]
    private void Duplicate()
    {
        var made = new List<(FxKind Kind, string Name)>();
        foreach (var row in _selected)
        {
            var taken = BuiltInNames(row.Kind).Concat(_store.Names(row.Kind.Kind)).ToList();
            var copy = PresetRules.CopyName(row.Name, taken);
            if (Try(() => _store.Save(row.Kind.Kind, copy, row.Content))) made.Add((row.Kind, copy));
        }
        SelectRequested?.Invoke(Rows.Where(r => made.Any(m => r.Is(m.Kind, m.Name))).ToList());
    }

    /// <summary>Deletes the selected presets of the user's own at once and offers Undo for a few seconds.</summary>
    [RelayCommand]
    private void Delete()
    {
        var doomed = _selected.Where(r => !r.BuiltIn).ToList();
        if (doomed.Count == 0) return;
        var undos = new List<Action>();
        foreach (var group in doomed.GroupBy(r => r.Kind.Kind))
        {
            Action? undo = null;
            if (Try(() => undo = _store.Delete(group.Key, group.Select(r => r.Name).ToList())) && undo is not null) undos.Add(undo);
        }
        if (undos.Count == 0) return;
        _undo = () => { foreach (var undo in undos) undo(); };
        UndoText = doomed.Count == 1 ? $"Deleted \"{doomed[0].Name}\"" : $"Deleted {doomed.Count} presets";
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    [RelayCommand]
    private void Undo()
    {
        var undo = _undo;
        ClearUndo();
        undo?.Invoke();
    }

    private void ClearUndo()
    {
        _undoTimer.Stop();
        _undo = null;
        UndoText = string.Empty;
    }

    /// <summary>Moves the selected presets of the user's own one row up (-1) or down (+1) as a block.</summary>
    public void Move(int delta)
    {
        if (IsSearching) return;
        var keep = _selected;
        foreach (var group in keep.Where(r => !r.BuiltIn).GroupBy(r => r.Kind.Kind))
            foreach (var (name, index) in PresetRules.BlockSteps(_store.Names(group.Key), group.Select(r => r.Name).ToList(), delta))
                Try(() => _store.Move(group.Key, name, index));
        SelectRequested?.Invoke(Rows.Where(r => keep.Any(k => r.Is(k.Kind, k.Name))).ToList());
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    // ---- import and export (the window does the file dialogs and the reading and writing) ----

    /// <summary>The bytes of the one selected preset for an export file, and the name to suggest for the file.</summary>
    public (string FileName, byte[] Bytes)? ExportOfSelected() =>
        _selected is [{ } row]
            ? (row.Name, UserPresetStore.Export(new UserPreset { Kind = row.Kind.Kind, Name = row.Name, Content = row.Content }))
            : null;

    /// <summary>Adds a preset from a file's bytes to the kind that is shown, under the file's name (a copy name when taken); says why not when it is no preset of this kind.</summary>
    public void Import(string fileName, byte[] bytes)
    {
        var kind = Node.Kind;
        if (UserPresetStore.Read(bytes) is not { } preset || preset.Kind != kind.Kind)
        {
            Say($"\"{fileName}\" is not a preset of {kind.Name}.");
            return;
        }
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (PresetRules.Error(name) is not null) name = ImportedName;
        var taken = BuiltInNames(kind).Concat(_store.Names(kind.Kind)).ToList();
        if (taken.Contains(name, StringComparer.OrdinalIgnoreCase)) name = PresetRules.CopyName(name, taken);
        if (Try(() => _store.Save(kind.Kind, name, preset.Content))) SelectRequested?.Invoke(Rows.Where(r => r.Is(kind, name)).ToList());
    }

    public void ReportFile(string text) => Say(text);

    private static IEnumerable<string> BuiltInNames(FxKind kind) => PresetCatalog.BuiltIns(kind).Select(b => b.Name);

    /// <summary>Runs a change; an input or output problem is said on the message line instead of crashing the window.</summary>
    private bool Try(Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say(ChangeFailed + ex.Message);
            return false;
        }
    }
}
