using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Volume.Audio;

/// One Volume preset: a snapshot of the Volume page's manual setup, the mixer settings that <see cref="AudioDefaults.Apply"/> covers
/// (every row's Method and Plays on, the master's method and devices, combined devices, effect chains, cable switches and marks). <see cref="Setup"/> null = empty: applying it gives the audio defaults.
public sealed class VolumePreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public MixerSettings? Setup { get; set; }
}

/// The Volume presets and the one in force, stored as JSON in %APPDATA%\OmniControl\volume-presets.json.
public sealed class VolumePresetFile
{
    private static readonly DataStore Store = DataStores.VolumePresets;

    /// The first name of a preset (the defaults are "Preset 1".."Preset 3", an added one the first free "Preset N").
    public const string NewName = "Preset";

    /// How many empty presets a new file starts with.
    private const int DefaultCount = 3;

    public List<VolumePreset> Presets { get; set; } = new();

    /// The preset in force: edits on the Volume page are saved into it.
    public string ActiveId { get; set; } = string.Empty;

    /// The saved file, or the defaults: three empty presets, the first in force.
    public static VolumePresetFile Load()
    {
        if (AppDataStore.Load<VolumePresetFile>(Store) is { Presets.Count: > 0 } file) return file;
        var fresh = new VolumePresetFile();
        for (var n = 1; n <= DefaultCount; n++) fresh.Presets.Add(new VolumePreset { Name = $"{NewName} {n}", Id = PresetRules.NewId() });
        fresh.ActiveId = fresh.Presets[0].Id;
        return fresh;
    }

    public void Save() => AppDataStore.SaveSoon(Store, this);
}
