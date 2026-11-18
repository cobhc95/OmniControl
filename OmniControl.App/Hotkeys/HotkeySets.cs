using OmniControl.App.Audio;
using OmniControl.App.Services;

namespace OmniControl.App.Hotkeys;

/// <summary>One named set of every hotkey binding (see <see cref="HotkeySets"/>); a plain class so the settings file reads and writes it.</summary>
public sealed class HotkeySet
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, StoredHotkey> Bindings { get; set; } = new();
}

/// <summary>What <c>hotkey-sets.json</c> holds.</summary>
public sealed class HotkeySetsFile
{
    public string CurrentId { get; set; } = string.Empty;
    public List<HotkeySet> Sets { get; set; } = new();
}

/// <summary>
/// The presets of the whole program's shortcuts: each is a name and every binding (<see cref="HotkeyRegistry.ExportStored"/>). The set
/// in use follows every edit of the bindings (they are saved into it, as other presets are), and switching puts a set's bindings in
/// force at once. The first run keeps the bindings of hotkeys.json as "Default set". No UI; the page's picker and the preset manager adapt it.
/// </summary>
public sealed class HotkeySets : IDisposable
{
    public const string FirstName = "Default set", NewName = "Shortcut set";

    private static readonly DataStore Store = DataStores.HotkeySets;

    private readonly HotkeyRegistry _registry;
    private readonly HotkeySetsFile _file;

    public HotkeySets(HotkeyRegistry registry)
    {
        _registry = registry;
        _file = AppDataStore.Load<HotkeySetsFile>(Store) is { Sets.Count: > 0 } saved ? saved : First(registry);
        if (_file.Sets.All(s => s.Id != _file.CurrentId)) _file.CurrentId = _file.Sets[0].Id;
        registry.BindingsSaved += OnBindingsSaved;
    }

    private static HotkeySetsFile First(HotkeyRegistry registry)
    {
        var set = new HotkeySet { Id = PresetRules.NewId(), Name = FirstName, Bindings = registry.ExportStored() };
        return new HotkeySetsFile { CurrentId = set.Id, Sets = { set } };
    }

    public IReadOnlyList<HotkeySet> Sets => _file.Sets;

    public string CurrentId => _file.CurrentId;

    public HotkeySet Current => _file.Sets.First(s => s.Id == _file.CurrentId);

    /// <summary>The sets, their order or the one in use changed.</summary>
    public event Action? Changed;

    public HotkeySet? Find(string id) => _file.Sets.FirstOrDefault(s => s.Id == id);

    /// <summary>Puts the set's bindings in force and makes it the set in use.</summary>
    public void Switch(string id)
    {
        if (Find(id) is not { } set || id == _file.CurrentId) return;
        _file.CurrentId = id;
        _registry.ImportStored(set.Bindings); // raises BindingsSaved, which writes the same bindings back into the set
        Saved();
    }

    /// <summary>Adds a set holding the bindings in force now, under this name.</summary>
    public HotkeySet Add(string name)
    {
        var set = new HotkeySet { Id = PresetRules.NewId(), Name = name, Bindings = _registry.ExportStored() };
        _file.Sets.Add(set);
        Saved();
        return set;
    }

    public void Rename(string id, string name)
    {
        if (Find(id) is not { } set) return;
        set.Name = name;
        Saved();
    }

    public void Move(string id, int index)
    {
        if (Find(id) is not { } set) return;
        _file.Sets.Remove(set);
        _file.Sets.Insert(Math.Clamp(index, 0, _file.Sets.Count), set);
        Saved();
    }

    /// <summary>Removes the set (never the last one); the result puts it back. Removing the set in use switches to the first other.</summary>
    public Action? Delete(string id)
    {
        if (_file.Sets.Count < 2 || Find(id) is not { } set) return null;
        var index = _file.Sets.IndexOf(set);
        _file.Sets.Remove(set);
        if (id == _file.CurrentId) Switch(_file.Sets[0].Id);
        else Saved();
        return () =>
        {
            _file.Sets.Insert(Math.Min(index, _file.Sets.Count), set);
            Saved();
        };
    }

    /// <summary>The bindings in force changed (or were imported): keep them in the set in use.</summary>
    private void OnBindingsSaved()
    {
        Current.Bindings = _registry.ExportStored();
        AppDataStore.Save(Store, _file);
    }

    /// <summary>
    /// Reads hotkey-sets.json again (an import from another PC wrote it): the sets and the one in use are replaced and the listeners told.
    /// The bindings in force are not touched (hotkeys.json is reloaded by the registry); with no sets in the file the ones here are kept.
    /// </summary>
    public bool ReloadFromDisk()
    {
        if (AppDataStore.Load<HotkeySetsFile>(Store) is not { Sets.Count: > 0 } saved) return true;
        _file.Sets = saved.Sets;
        _file.CurrentId = saved.Sets.Any(s => s.Id == saved.CurrentId) ? saved.CurrentId : saved.Sets[0].Id;
        Changed?.Invoke();
        return true;
    }

    private void Saved()
    {
        AppDataStore.Save(Store, _file);
        Changed?.Invoke();
    }

    public void Dispose() => _registry.BindingsSaved -= OnBindingsSaved;
}
