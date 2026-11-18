using OmniControl.App.Display;

namespace OmniControl.App.Audio;

/// <summary>
/// The Volume presets, like the mouse layers: one is always in force, and every change of the Volume page's setup is saved into it at
/// once. Choosing another preset applies its setup live through <see cref="AudioDefaults.Apply"/> (the same single step the restore of
/// the audio defaults uses, so the engine reacts as to the same change made by hand); an empty preset applies the audio defaults.
/// Names, ids and order follow the shared <see cref="PresetList{T}"/> rules. No UI.
/// </summary>
public sealed class VolumePresets : IDisposable
{
    private readonly MixerService _mixer;
    private readonly VolumePresetFile _file;
    private bool _applying;

    public VolumePresets(MixerService mixer)
    {
        _mixer = mixer;
        _file = VolumePresetFile.Load();
        List = new PresetList<VolumePreset>(_file.Presets, _file.Save);
        if (Active is null) _file.ActiveId = _file.Presets[0].Id;
        List.Changed += OnListChanged;
        mixer.Updated += Capture;
        mixer.FxChanged += OnFxChanged;
        mixer.CablesChanged += Capture;
        Capture();
    }

    /// <summary>The names, ids and order (rename, move, delete, add go through it).</summary>
    public PresetList<VolumePreset> List { get; }

    public IReadOnlyList<VolumePreset> Presets => _file.Presets;

    /// <summary>The preset in force (null only while the list is being changed).</summary>
    public VolumePreset? Active => _file.Presets.FirstOrDefault(p => p.Id == _file.ActiveId);

    /// <summary>The list, the preset in force or a preset's setup changed (UI thread).</summary>
    public event Action? Changed;

    /// <summary>Makes the preset the one in force and applies its setup (an empty one: the audio defaults).</summary>
    public void Apply(string id)
    {
        if (_file.Presets.FirstOrDefault(p => p.Id == id) is not { } preset || preset == Active) return;
        Capture(); // the setup being left is kept in its preset first
        _file.ActiveId = id;
        _applying = true;
        try
        {
            var target = preset.Setup is { } setup ? AudioDefaults.Snapshot(setup) : AudioDefaults.DefaultsOf(_mixer.Settings);
            AudioDefaults.Apply(new MixerDefaultsStore(_mixer), target);
        }
        finally { _applying = false; }
        Store(preset);
    }

    /// <summary>
    /// Takes volume-presets.json from disk as it is now (an import wrote it): the presets and the one in force are replaced in the list the
    /// rules hold, and the listeners told. Nothing is applied: the mixer's own file arrived with it.
    /// </summary>
    public bool ReloadFromDisk()
    {
        var fresh = VolumePresetFile.Load();
        _file.Presets.Clear();
        _file.Presets.AddRange(fresh.Presets);
        _file.ActiveId = fresh.Presets.Any(p => p.Id == fresh.ActiveId) ? fresh.ActiveId : fresh.Presets[0].Id;
        List.NotifyChanged();
        return true;
    }

    /// <summary>Adds an empty preset under the first free "Preset N"; returns its id.</summary>
    public string Add()
    {
        var name = PresetRules.FirstFree(VolumePresetFile.NewName, _file.Presets.Select(p => p.Name).ToList());
        List.Save(new VolumePreset { Name = name });
        return List.Find(name)!.Id;
    }

    private void OnFxChanged(string? key) => Capture();

    private void OnListChanged() => Changed?.Invoke();

    /// <summary>An edit on the Volume page: the live setup is saved into the preset in force (only when it differs, so a scan writes nothing).</summary>
    private void Capture()
    {
        if (_applying || _mixer.IsReloading || Active is not { } active) return;
        if (active.Setup is { } kept && AudioDefaults.SameSetup(kept, _mixer.Settings)) return;
        Store(active);
    }

    private void Store(VolumePreset preset)
    {
        preset.Setup = AudioDefaults.Snapshot(_mixer.Settings);
        _file.Save();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        List.Changed -= OnListChanged;
        _mixer.Updated -= Capture;
        _mixer.FxChanged -= OnFxChanged;
        _mixer.CablesChanged -= Capture;
    }
}
