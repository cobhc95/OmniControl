using System.Text.Json;
using OmniControl.App.Services;

namespace OmniControl.App.Audio;

/// <summary>
/// The user's own presets of every effect kind, in presets.json (through <see cref="AppDataStore"/>). A preset belongs to one kind
/// (<see cref="UserPreset.Kind"/>), because a 16-band and a 32-band equaliser, or an equaliser and a compressor, hold different things;
/// the order among one kind's presets is the order shown; each has a stable id (a preset's shortcut follows it). The built-in presets
/// stay in the catalog (<see cref="PresetCatalog"/>) and are never stored here. No UI.
/// </summary>
public sealed class UserPresetStore
{
    private static readonly DataStore Store = DataStores.EffectPresets;

    /// <summary>The ending of an exported preset file (without the dot).</summary>
    public const string FileExtension = "json";

    /// <summary>What the file holds (an object, so more can be added beside the list later).</summary>
    public sealed class Stored
    {
        public List<UserPreset> Presets { get; set; } = new();

        /// <summary>Device folders saved as presets (<see cref="FolderPreset"/>).</summary>
        public List<FolderPreset> Folders { get; set; } = new();
    }

    private readonly Stored _data;
    private readonly Action<Stored> _save;

    /// <param name="data">What was saved (null: nothing yet).</param>
    /// <param name="save">Writes the presets; a test gives its own so nothing is written under the user's folder.</param>
    public UserPresetStore(Stored? data, Action<Stored> save)
    {
        _data = data ?? new Stored();
        _save = save;
        Tidy(_data, _save);
    }

    /// <summary>A damaged or hand-edited entry is dropped rather than shown: the catalog says what each kind can hold; an older file's presets get their ids (saved once so they stay).</summary>
    private static void Tidy(Stored data, Action<Stored> save)
    {
        data.Presets ??= new List<UserPreset>();
        data.Presets.RemoveAll(p => p is null || FxCatalog.All.FirstOrDefault(k => k.Kind == p.Kind) is not { } kind
            || PresetRules.Error(p.Name ?? "") is not null || p.Content is null || PresetCatalog.Fit(kind, p.Content) is null);
        data.Folders ??= new List<FolderPreset>();
        data.Folders.RemoveAll(f => f is null || PresetRules.Error(f.Name ?? "") is not null || f.Effects is null || f.Effects.Any(e => e is null || e.IsFolder));
        var gaveIds = false;
        foreach (var preset in data.Presets.Where(p => string.IsNullOrEmpty(p.Id)))
        {
            preset.Id = PresetRules.NewId();
            gaveIds = true;
        }
        foreach (var folder in data.Folders.Where(f => string.IsNullOrEmpty(f.Id)))
        {
            folder.Id = PresetRules.NewId();
            gaveIds = true;
        }
        if (gaveIds) save(data); // the ids of an older file's presets stay the same from now on
    }

    /// <summary>The store as the program uses it: read from and written to the user's folder.</summary>
    public static UserPresetStore OnDisk() => new(AppDataStore.Load<Stored>(Store), data => AppDataStore.Save(Store, data));

    /// <summary>Takes presets.json from disk as it is now (an import wrote it): the presets and device folders are replaced and the listeners told.</summary>
    public bool ReloadFromDisk()
    {
        var fresh = AppDataStore.Load<Stored>(Store) ?? new Stored();
        Tidy(fresh, _save);
        SettingsCopy.Into(_data, fresh);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Something in the list changed (raised on the thread that changed it, the UI thread).</summary>
    public event Action? Changed;

    /// <summary>The kind's presets in their order.</summary>
    public IReadOnlyList<UserPreset> Of(string kind) => _data.Presets.Where(p => p.Kind == kind).ToList();

    public IReadOnlyList<string> Names(string kind) => Of(kind).Select(p => p.Name).ToList();

    public UserPreset? Find(string kind, string name) => _data.Presets.FirstOrDefault(p => p.Kind == kind && PresetRules.Same(p.Name, name));

    public UserPreset? FindById(string id) => _data.Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Keeps the content as the preset of that name: a preset of the same name (any case) is replaced where it stands and keeps its id,
    /// else it goes last with a new one. Returns the preset's id.
    /// </summary>
    public string Save(string kind, string name, PresetContent content)
    {
        name = name.Trim();
        var fit = PresetCatalog.Fit(FxCatalog.All.First(k => k.Kind == kind), content) ?? throw new InvalidDataException("This is not a preset of this effect.");
        var at = _data.Presets.FindIndex(p => p.Kind == kind && PresetRules.Same(p.Name, name));
        var preset = new UserPreset { Kind = kind, Name = name, Content = fit, Id = at >= 0 ? _data.Presets[at].Id : PresetRules.NewId() };
        if (at >= 0) _data.Presets[at] = preset;
        else _data.Presets.Add(preset);
        Commit();
        return preset.Id!;
    }

    /// <summary>Renames a preset (the caller has checked the name is free).</summary>
    public void Rename(string kind, string from, string to)
    {
        if (Find(kind, from) is not { } preset) return;
        preset.Name = to.Trim();
        Commit();
    }

    /// <summary>Puts a preset at this place among its kind's presets.</summary>
    public void Move(string kind, string name, int index)
    {
        var ordered = Of(kind).ToList();
        var from = ordered.FindIndex(p => PresetRules.Same(p.Name, name));
        if (from < 0) return;
        var preset = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), preset);
        // The kind's presets keep the places the kind held in the whole list.
        var slots = _data.Presets.Select((p, i) => (p, i)).Where(x => x.p.Kind == kind).Select(x => x.i).ToList();
        for (var i = 0; i < slots.Count; i++) _data.Presets[slots[i]] = ordered[i];
        Commit();
    }

    /// <summary>Deletes presets; the result puts them back where they were (null when nothing was deleted).</summary>
    public Action? Delete(string kind, IReadOnlyList<string> names)
    {
        var gone = _data.Presets.Select((p, i) => (p, i)).Where(x => x.p.Kind == kind && names.Any(n => PresetRules.Same(n, x.p.Name))).ToList();
        if (gone.Count == 0) return null;
        _data.Presets.RemoveAll(p => gone.Any(g => ReferenceEquals(g.p, p)));
        Commit();
        return () =>
        {
            foreach (var (preset, index) in gone)
            {
                if (Find(kind, preset.Name) is not null) continue; // the name was taken meanwhile: keep the newer one
                _data.Presets.Insert(Math.Min(index, _data.Presets.Count), preset);
            }
            Commit();
        };
    }

    /// <summary>One preset as the bytes of an export file (its id stays at home: an imported preset gets its own).</summary>
    public static byte[] Export(UserPreset preset) =>
        JsonSerializer.SerializeToUtf8Bytes(new UserPreset { Kind = preset.Kind, Name = preset.Name, Content = preset.Content, Saved = preset.Saved },
            new JsonSerializerOptions { WriteIndented = true });

    /// <summary>The preset an export file holds; null when the bytes are no preset of a kind this version knows.</summary>
    public static UserPreset? Read(byte[] bytes)
    {
        try
        {
            if (JsonSerializer.Deserialize<UserPreset>(bytes) is not { } preset || FxCatalog.All.FirstOrDefault(k => k.Kind == preset.Kind) is not { } kind) return null;
            return preset.Content is not null && PresetCatalog.Fit(kind, preset.Content) is { } fit ? new UserPreset { Kind = kind.Kind, Name = preset.Name, Content = fit } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- device folders as presets: their own list, in their own order, with their own names ----

    /// <summary>The folder presets in their order.</summary>
    public IReadOnlyList<FolderPreset> Folders => _data.Folders;

    public FolderPreset? FindFolder(string id) => _data.Folders.FirstOrDefault(f => f.Id == id);

    /// <summary>Keeps a copy of the folder as the preset of that name (same name, any case: replaced where it stands, keeping its id; else last). Returns its id.</summary>
    public string SaveFolder(string name, FolderPreset content)
    {
        name = name.Trim();
        var at = _data.Folders.FindIndex(f => PresetRules.Same(f.Name, name));
        var preset = content.Copy();
        preset.Name = name;
        preset.Id = at >= 0 ? _data.Folders[at].Id : PresetRules.NewId();
        preset.Saved = DateTime.Now;
        if (at >= 0) _data.Folders[at] = preset;
        else _data.Folders.Add(preset);
        Commit();
        return preset.Id!;
    }

    /// <summary>Renames a folder preset (the caller has checked the name is free).</summary>
    public void RenameFolder(string id, string to)
    {
        if (FindFolder(id) is not { } preset) return;
        preset.Name = to.Trim();
        Commit();
    }

    /// <summary>Puts a folder preset at this place among the folder presets.</summary>
    public void MoveFolder(string id, int index)
    {
        var from = _data.Folders.FindIndex(f => f.Id == id);
        if (from < 0) return;
        var preset = _data.Folders[from];
        _data.Folders.RemoveAt(from);
        _data.Folders.Insert(Math.Clamp(index, 0, _data.Folders.Count), preset);
        Commit();
    }

    /// <summary>Deletes a folder preset; the result puts it back where it was (null when there was none).</summary>
    public Action? DeleteFolder(string id)
    {
        var at = _data.Folders.FindIndex(f => f.Id == id);
        if (at < 0) return null;
        var preset = _data.Folders[at];
        _data.Folders.RemoveAt(at);
        Commit();
        return () =>
        {
            if (_data.Folders.Any(f => PresetRules.Same(f.Name, preset.Name))) return; // the name was taken meanwhile: keep the newer one
            _data.Folders.Insert(Math.Min(at, _data.Folders.Count), preset);
            Commit();
        };
    }

    private void Commit()
    {
        _save(_data);
        Changed?.Invoke();
    }
}
