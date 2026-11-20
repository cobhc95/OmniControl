using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// THE favourite actions of every Choose-the-action window (starred entries, by <see cref="PickChoice.Spec"/>, in the order they were starred),
/// kept in favourite-actions.json. Read once at start-up and held here, so a picker never reads the disk; follows moved action ids.
public sealed class FavouriteActions : IDisposable
{
    private static readonly DataStore Store = DataStores.FavouriteActions;
    private readonly IActionCatalogue _actions;
    private List<string> _specs;

    public FavouriteActions(IActionCatalogue actions)
    {
        _actions = actions;
        _specs = Read();
        actions.Moved += OnMoved;
    }

    /// The starred entries, oldest first.
    public IReadOnlyList<string> Specs => _specs;

    public bool Contains(string spec) => _specs.Contains(spec);

    /// Stars (or un-stars) these entries together (every direction of a family is one star).
    public void Set(IEnumerable<string> specs, bool on)
    {
        var given = specs.ToList();
        _specs = on ? _specs.Concat(given.Where(s => !_specs.Contains(s))).ToList() : _specs.Where(s => !given.Contains(s)).ToList();
        AppDataStore.SaveSoon(Store, _specs);
    }

    /// Takes the file as it is now (an import wrote it).
    public bool ReloadFromDisk()
    {
        _specs = Read();
        return true;
    }

    private static List<string> Read() => AppDataStore.Load<List<string>>(Store) ?? new();

    private void OnMoved(IReadOnlyDictionary<string, string> moved)
    {
        var specs = _specs.ToList();
        if (!MovedActions.RewriteAll(specs, moved)) return;
        _specs = specs.Distinct().ToList();
        AppDataStore.SaveSoon(Store, _specs);
    }

    public void Dispose() => _actions.Moved -= OnMoved;
}
