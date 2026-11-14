using OmniControl.App.Audio;
using OmniControl.App.Services;

namespace OmniControl.App.Display;

/// <summary>
/// The Displays page's saved settings (<see cref="DisplaySettings"/>) and the rules for the preset list: names are unique ignoring
/// case and follow <see cref="PresetRules"/>. Every change is saved at once through the function given. No UI.
/// </summary>
public sealed class DisplaySettingsStore
{
    private const string FileName = "display-settings.json";

    private readonly Action<DisplaySettings> _save;

    /// <param name="data">What was saved (null: nothing yet).</param>
    /// <param name="save">Writes the settings; a test gives its own so nothing is written under the user's folder.</param>
    public DisplaySettingsStore(DisplaySettings? data, Action<DisplaySettings> save)
    {
        Data = data ?? new DisplaySettings();
        _save = save;
        // A damaged or hand-edited entry is dropped rather than shown.
        Data.Presets.RemoveAll(p => p is null || PresetRules.Error(p.Name ?? string.Empty) is not null || p.Displays is null || p.Any is null);
        Data.WarmLight ??= new WarmLightSettings();
    }

    /// <summary>The store as the program uses it: read from and written to the user's folder.</summary>
    public static DisplaySettingsStore OnDisk() =>
        new(AppDataStore.Load<DisplaySettings>(FileName), settings => AppDataStore.Save(FileName, settings));

    public DisplaySettings Data { get; }

    public WarmLightSettings WarmLight => Data.WarmLight;

    /// <summary>The preset list changed (raised on the thread that changed it, the UI thread).</summary>
    public event Action? PresetsChanged;

    public IReadOnlyList<DisplayPreset> Presets => Data.Presets;

    public DisplayPreset? Find(string name) => Data.Presets.FirstOrDefault(p => PresetRules.Same(p.Name, name));

    /// <summary>Saves the warm-light settings after a change.</summary>
    public void SaveWarmLight() => _save(Data);

    /// <summary>Keeps the preset: one of the same name (any case) is replaced where it stands, else it goes last.</summary>
    public void Save(DisplayPreset preset)
    {
        preset.Name = preset.Name.Trim();
        var at = Data.Presets.FindIndex(p => PresetRules.Same(p.Name, preset.Name));
        if (at >= 0) Data.Presets[at] = preset;
        else Data.Presets.Add(preset);
        Commit();
    }

    /// <summary>Renames a preset. Returns why it cannot be (a bad name, or another preset has it), or null when done.</summary>
    public string? Rename(string from, string to)
    {
        if (PresetRules.Error(to) is { } problem) return problem;
        if (Find(from) is not { } preset) return "That preset is gone.";
        if (Data.Presets.Any(p => !ReferenceEquals(p, preset) && PresetRules.Same(p.Name, to))) return "A preset of this name exists.";
        preset.Name = to.Trim();
        Commit();
        return null;
    }

    public void Delete(string name)
    {
        if (Data.Presets.RemoveAll(p => PresetRules.Same(p.Name, name)) > 0) Commit();
    }

    private void Commit()
    {
        _save(Data);
        PresetsChanged?.Invoke();
    }
}
