using OmniControl.App.Services;

namespace OmniControl.App.SettingsImport;

/// <summary>
/// What the owners of stores tell an import, each registered by the owner and never listed here: how a store is reloaded live
/// (<see cref="OnReload"/>; an area is applied live only when every one of its stores can) and a fact worth showing after an area
/// (<see cref="OnFact"/>, such as how many smart devices still need a password).
/// </summary>
public sealed class ImportHooks
{
    private readonly Dictionary<string, Func<bool>> _reloads = new();
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, string>, string?>> _facts = new();

    /// <param name="reload">Reads the store's saved file again into the owner and returns whether it now holds it.</param>
    public void OnReload(DataStore store, Func<bool> reload) => _reloads[store.FileName] = reload;

    /// <param name="fact">Gets the area's imported files (by file name) and gives a short fact such as "2 need passwords", or null.</param>
    public void OnFact(string area, Func<IReadOnlyDictionary<string, string>, string?> fact) => _facts[area] = fact;

    public bool CanReload(DataStore store) => _reloads.ContainsKey(store.FileName);

    /// <summary>Reloads the store through its owner; false when it has no owner hook or the reload failed (logged).</summary>
    public bool Reload(DataStore store)
    {
        if (!_reloads.TryGetValue(store.FileName, out var reload)) return false;
        var done = false;
        return Guard.Run($"Import: reloading {store.FileName}", () => done = reload()) is null && done;
    }

    /// <summary>The owner's fact about the files just imported into the area, or null.</summary>
    public string? FactAbout(string area, IReadOnlyDictionary<string, string> files)
    {
        if (!_facts.TryGetValue(area, out var fact)) return null;
        string? text = null;
        Guard.Run($"Import: fact about {area}", () => text = fact(files));
        return text;
    }
}
