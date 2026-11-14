using System.Text.Json;
using OmniControl.App.Services;

namespace OmniControl.App.Audio;

/// <summary>
/// The user's own presets of every effect kind, in presets.json (through <see cref="AppDataStore"/>). A preset belongs to one kind
/// (<see cref="UserPreset.Kind"/>), because a 16-band and a 32-band equaliser, or an equaliser and a compressor, hold different things;
/// the order among one kind's presets is the order shown. The built-in presets stay in the catalog (<see cref="PresetCatalog"/>) and are
/// never stored here. No UI.
/// </summary>
public sealed class UserPresetStore
{
    private const string FileName = "presets.json";

    /// <summary>The ending of an exported preset file (without the dot).</summary>
    public const string FileExtension = "json";

    /// <summary>What the file holds (an object, so more can be added beside the list later).</summary>
    public sealed class Stored
    {
        public List<UserPreset> Presets { get; set; } = new();
    }

    private readonly Stored _data;

    public UserPresetStore()
    {
        _data = AppDataStore.Load<Stored>(FileName) ?? new Stored();
        // A damaged or hand-edited entry is dropped rather than shown: the catalog says what each kind can hold.
        _data.Presets.RemoveAll(p => p is null || FxCatalog.All.FirstOrDefault(k => k.Kind == p.Kind) is not { } kind
            || PresetRules.Error(p.Name ?? "") is not null || p.Content is null || PresetCatalog.Fit(kind, p.Content) is null);
    }

    /// <summary>Something in the list changed (raised on the thread that changed it, the UI thread).</summary>
    public event Action? Changed;

    /// <summary>The kind's presets in their order.</summary>
    public IReadOnlyList<UserPreset> Of(string kind) => _data.Presets.Where(p => p.Kind == kind).ToList();

    public IReadOnlyList<string> Names(string kind) => Of(kind).Select(p => p.Name).ToList();

    public UserPreset? Find(string kind, string name) => _data.Presets.FirstOrDefault(p => p.Kind == kind && PresetRules.Same(p.Name, name));

    /// <summary>Keeps the content as the preset of that name: a preset of the same name (any case) is replaced where it stands, else it goes last.</summary>
    public void Save(string kind, string name, PresetContent content)
    {
        name = name.Trim();
        var fit = PresetCatalog.Fit(FxCatalog.All.First(k => k.Kind == kind), content) ?? throw new InvalidDataException("This is not a preset of this effect.");
        var preset = new UserPreset { Kind = kind, Name = name, Content = fit };
        var at = _data.Presets.FindIndex(p => p.Kind == kind && PresetRules.Same(p.Name, name));
        if (at >= 0) _data.Presets[at] = preset;
        else _data.Presets.Add(preset);
        Commit();
    }

    /// <summary>Renames a preset; with <paramref name="replace"/> another of the new name (any case) is removed first.</summary>
    public void Rename(string kind, string from, string to, bool replace)
    {
        if (Find(kind, from) is not { } preset) return;
        if (replace) _data.Presets.RemoveAll(p => p.Kind == kind && !ReferenceEquals(p, preset) && PresetRules.Same(p.Name, to));
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

    /// <summary>One preset as the bytes of an export file.</summary>
    public static byte[] Export(UserPreset preset) => JsonSerializer.SerializeToUtf8Bytes(preset, new JsonSerializerOptions { WriteIndented = true });

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

    private void Commit()
    {
        AppDataStore.Save(FileName, _data);
        Changed?.Invoke();
    }
}
