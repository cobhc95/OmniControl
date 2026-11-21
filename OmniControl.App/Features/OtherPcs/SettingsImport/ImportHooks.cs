namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// What the owners of stores tell an import, each registered by the owner and never listed here: how a store is reloaded live
/// (<see cref="OnReload"/>; an area is applied live only when every one of its stores can) and a fact worth showing after an area
/// (<see cref="OnFact"/>, such as how many smart devices still need a password).
public sealed class ImportHooks
{
    private readonly Dictionary<string, Func<bool>> _reloads;
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, string>, string?>> _facts;
    private readonly List<string> _ownReloads = new(), _ownFacts = new();

    public ImportHooks() => (_reloads, _facts) = (new(), new());

    private ImportHooks(ImportHooks shared) => (_reloads, _facts) = (shared._reloads, shared._facts);

    /// A view of the same hooks for one feature that can be switched off: <see cref="Forget"/> takes back what it registered.
    public ImportHooks Owned() => new(this);

    /// <param name="reload">Reads the store's saved file again into the owner and returns whether it now holds it.</param>
    public void OnReload(DataStore store, Func<bool> reload)
    {
        _reloads[store.FileName] = reload;
        _ownReloads.Add(store.FileName);
    }

    /// <param name="fact">Gets the area's imported files (by file name) and gives a short fact such as "2 need passwords", or null.</param>
    public void OnFact(string area, Func<IReadOnlyDictionary<string, string>, string?> fact)
    {
        _facts[area] = fact;
        _ownFacts.Add(area);
    }

    /// Takes out the hooks registered through this view (its feature was switched off: an import then says its stores apply at the next start).
    public void Forget()
    {
        foreach (var file in _ownReloads) _reloads.Remove(file);
        foreach (var area in _ownFacts) _facts.Remove(area);
        _ownReloads.Clear();
        _ownFacts.Clear();
    }

    public bool CanReload(DataStore store) => _reloads.ContainsKey(store.FileName);

    /// Reloads the store through its owner; false when it has no owner hook or the reload failed (logged).
    public bool Reload(DataStore store)
    {
        if (!_reloads.TryGetValue(store.FileName, out var reload)) return false;
        var done = false;
        return Guard.Run($"Import: reloading {store.FileName}", () => done = reload()) is null && done;
    }

    /// The owner's fact about the files just imported into the area, or null.
    public string? FactAbout(string area, IReadOnlyDictionary<string, string> files)
    {
        if (!_facts.TryGetValue(area, out var fact)) return null;
        string? text = null;
        Guard.Run($"Import: fact about {area}", () => text = fact(files));
        return text;
    }
}
