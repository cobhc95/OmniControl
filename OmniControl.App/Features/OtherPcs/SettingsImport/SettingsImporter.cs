using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// How one area ended.
public enum AreaOutcome
{
    /// Saved atomically, read back equal to what arrived, and reloaded by every owner: it is in force now.
    Checked,

    /// Not applied; what was here is kept.
    Failed,
}

/// The end of one area: how it ended, a fact worth showing ("2 need passwords") and, when it failed, the one plain sentence why.
public sealed record AreaResult(string Area, AreaOutcome Outcome, string? Fact = null, string? Why = null);

/// Carries out an import of chosen areas from another PC, one area at a time, each on its own: an automatic backup first (the existing
/// <see cref="BackupFile"/>), then per area the files that arrived are checked (a store of the area only, valid json, the properties a file
/// may not set kept as they are here), saved atomically through <see cref="AppDataStore"/>, read back and then reloaded by the owner of each
/// store (<see cref="ImportHooks"/>), which replaces what it holds in memory (so what it saves at exit is the imported value, never the old
/// one) and applies it. An area whose stores cannot all be reloaded is not applied at all. A failed area is put back alone and reloaded again.
/// <see cref="Undo"/> restores the backup the same way.
public sealed class SettingsImporter
{
    private readonly ImportHooks _hooks;
    private readonly List<ImportArea> _applied = new();

    public SettingsImporter(ImportHooks hooks) => _hooks = hooks;

    public string BackupPath => AppDataStore.PathOf(BackupFile.BeforeImportName);

    /// True once an area was applied, so <see cref="Undo"/> has something to put back.
    public bool HasApplied => _applied.Count > 0;

    /// Takes the automatic backup; null when it is made, else the one plain sentence why not (nothing may be imported then).
    public string? MakeBackup()
    {
        try
        {
            AppDataStore.FlushSaves(); // the backup reads the files: waiting saves first
            BackupFile.Write(AppDataStore.Folder, BackupPath, AppInfo.Version);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"The backup of this PC could not be saved ({ex.Message}). Nothing was imported.";
        }
    }

    /// Applies one area from the files another PC sent (by file name).
    public AreaResult Apply(ImportArea area, IReadOnlyDictionary<string, string> incoming)
    {
        var parts = new List<(DataStore Store, string Text)>();
        foreach (var store in area.Stores)
        {
            if (!incoming.TryGetValue(store.FileName, out var text)) continue; // only this area's own files are ever taken
            if (!BackupFile.TryParse(text, out var node)) return Failed(area, "What arrived could not be read.");
            if (store.NotImportedInside is { } inside) JsonMerge.StripInside(node, inside);
            var here = AppDataStore.LoadText(store) is { } own && BackupFile.TryParse(own, out var ownNode) ? ownNode : null;
            parts.Add((store, JsonMerge.KeepOwn(node, here, store.NotImported ?? Array.Empty<string>())?.ToJsonString() ?? text));
        }
        if (parts.Count == 0) return Failed(area, "That PC had nothing saved here. Yours is kept.");
        var fact = _hooks.FactAbout(area.Name, parts.ToDictionary(p => p.Store.FileName, p => p.Text));
        if (parts.Any(p => !_hooks.CanReload(p.Store))) return Failed(area, "OmniControl cannot take this area in while it runs. Yours is kept.");
        return ApplyLive(area, parts, fact);
    }

    private AreaResult ApplyLive(ImportArea area, List<(DataStore Store, string Text)> parts, string? fact)
    {
        var before = parts.Select(p => (p.Store, Text: AppDataStore.LoadText(p.Store))).ToList();
        string? why = null;
        foreach (var (store, text) in parts)
        {
            if (!AppDataStore.SaveText(store, text)) why = "The file could not be saved.";
            else if (AppDataStore.LoadText(store) != text) why = "What was saved did not read back the same.";
            if (why is not null) break;
        }
        if (why is null && parts.Any(p => !_hooks.Reload(p.Store))) why = "OmniControl could not load it.";
        if (why is null)
        {
            _applied.Add(area);
            return new AreaResult(area.Name, AreaOutcome.Checked, fact);
        }
        foreach (var (store, text) in before) PutBack(store, text);
        foreach (var (store, _) in before) _hooks.Reload(store);
        return Failed(area, why);
    }

    /// Puts everything back from the automatic backup: every applied area is written back and reloaded. Null when done, else why not.
    public string? Undo()
    {
        var (contents, problem) = BackupFile.Read(BackupPath);
        if (contents is null) return problem ?? "The backup of this PC could not be read.";
        string? failed = null;
        foreach (var area in _applied.AsEnumerable().Reverse())
        {
            foreach (var store in area.Stores) PutBack(store, contents.Files.GetValueOrDefault(store.FileName));
            foreach (var store in area.Stores)
                if (!_hooks.Reload(store)) failed = $"{area.Name} could not be loaded again; restart OmniControl to finish putting it back.";
        }
        _applied.Clear();
        return failed;
    }

    private static void PutBack(DataStore store, string? text)
    {
        if (text is null) AppDataStore.Delete(store);
        else AppDataStore.SaveText(store, text);
    }

    private static AreaResult Failed(ImportArea area, string why) => new(area.Name, AreaOutcome.Failed, Why: why);
}
