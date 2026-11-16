using OmniControl.App.Audio;

namespace OmniControl.App.Display;

/// <summary>A saved preset of any kind: all that the list rules need to know is its name and its stable id.</summary>
public interface INamedPreset
{
    string Name { get; set; }

    /// <summary>Never changes once given (empty in a file saved before presets had ids; the list gives one on load).</summary>
    string Id { get; set; }
}

/// <summary>
/// The rules of one list of named presets (picture presets, monitor presets): names are unique ignoring case and follow
/// <see cref="PresetRules"/>; each preset has a stable id; a changed list is saved at once through the function given. No UI.
/// </summary>
public sealed class PresetList<T> where T : class, INamedPreset
{
    private readonly List<T> _items;
    private readonly Action _commit;
    private readonly Action<string, string>? _renamed;
    private readonly Action<string>? _deleted;

    /// <param name="items">The list that is saved (changed in place).</param>
    /// <param name="commit">Saves the settings the list belongs to.</param>
    /// <param name="renamed">Told (old name, new name) before the list is saved, so what refers to the name can follow.</param>
    /// <param name="deleted">Told the name before the list is saved.</param>
    public PresetList(List<T> items, Action commit, Action<string, string>? renamed = null, Action<string>? deleted = null)
    {
        (_items, _commit, _renamed, _deleted) = (items, commit, renamed, deleted);
        foreach (var preset in _items.Where(p => string.IsNullOrEmpty(p.Id)))
        {
            preset.Id = PresetRules.NewId();
            GaveIds = true;
        }
    }

    /// <summary>True when presets of an older file were given their ids on load (the owner saves once so they stay).</summary>
    public bool GaveIds { get; }

    /// <summary>The list changed (raised on the thread that changed it, the UI thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<T> Items => _items;

    public T? Find(string name) => _items.FirstOrDefault(p => PresetRules.Same(p.Name, name));

    /// <summary>Keeps the preset: one of the same name (any case) is replaced where it stands and keeps its id, else it goes last with a new id.</summary>
    public void Save(T preset)
    {
        preset.Name = preset.Name.Trim();
        var at = _items.FindIndex(p => PresetRules.Same(p.Name, preset.Name));
        preset.Id = at >= 0 ? _items[at].Id : PresetRules.NewId();
        if (at >= 0) _items[at] = preset;
        else _items.Add(preset);
        Commit();
    }

    /// <summary>Renames a preset. Returns why it cannot be (a bad name, or another preset has it), or null when done.</summary>
    public string? Rename(string from, string to)
    {
        if (PresetRules.Error(to) is { } problem) return problem;
        if (Find(from) is not { } preset) return "That preset is gone.";
        if (_items.Any(p => !ReferenceEquals(p, preset) && PresetRules.Same(p.Name, to))) return "A preset of this name exists.";
        var old = preset.Name;
        preset.Name = to.Trim();
        _renamed?.Invoke(old, preset.Name);
        Commit();
        return null;
    }

    /// <summary>Puts a preset at this place in the list (clamped to the list).</summary>
    public void Move(string name, int index)
    {
        if (Find(name) is not { } preset) return;
        _items.Remove(preset);
        _items.Insert(Math.Clamp(index, 0, _items.Count), preset);
        Commit();
    }

    /// <summary>Deletes a preset; the result puts it back where it was (null when there was nothing to delete).</summary>
    public Action? Delete(string name)
    {
        if (Find(name) is not { } preset) return null;
        var index = _items.IndexOf(preset);
        _items.Remove(preset);
        _deleted?.Invoke(preset.Name);
        Commit();
        return () =>
        {
            if (Find(preset.Name) is not null) return; // the name was taken meanwhile: keep the newer one
            _items.Insert(Math.Min(index, _items.Count), preset);
            Commit();
        };
    }

    private void Commit()
    {
        _commit();
        Changed?.Invoke();
    }
}
