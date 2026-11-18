using OmniControl.App.Audio;
using OmniControl.App.Services;

namespace OmniControl.App.Display;

/// <summary>
/// The Displays page's saved settings (<see cref="DisplaySettings"/>) and the two preset lists (picture presets, monitor presets; the
/// rules are <see cref="PresetList{T}"/>'s). The monitor preset applied to each monitor last is kept by the monitor's model name.
/// Every change is saved at once through the function given. No UI.
/// </summary>
public sealed class DisplaySettingsStore
{
    private static readonly DataStore Store = DataStores.Displays;

    private readonly Action<DisplaySettings> _save;
    private readonly PresetList<DisplayPreset> _pictures;

    /// <param name="data">What was saved (null: nothing yet).</param>
    /// <param name="save">Writes the settings; a test gives its own so nothing is written under the user's folder.</param>
    public DisplaySettingsStore(DisplaySettings? data, Action<DisplaySettings> save)
    {
        Data = Tidy(data ?? new DisplaySettings());
        _save = save;

        _pictures = new PresetList<DisplayPreset>(Data.Presets, () => _save(Data));
        Monitors = new PresetList<MonitorPreset>(Data.MonitorPresets, () => _save(Data), ForgetRenamed, ForgetDeleted);
        if (_pictures.GaveIds || Monitors.GaveIds) _save(Data); // the ids of an older file's presets stay the same from now on
    }

    /// <summary>A damaged or hand-edited entry is dropped rather than shown.</summary>
    private static DisplaySettings Tidy(DisplaySettings data)
    {
        data.Presets ??= new List<DisplayPreset>();
        data.Presets.RemoveAll(p => p is null || PresetRules.Error(p.Name ?? string.Empty) is not null || p.Displays is null || p.Any is null);
        data.MonitorPresets ??= new List<MonitorPreset>();
        data.MonitorPresets.RemoveAll(p => p is null || PresetRules.Error(p.Name ?? string.Empty) is not null || p.Monitors is null);
        foreach (var preset in data.MonitorPresets)
        {
            preset.Monitors.RemoveAll(m => m is null || m.Values is null);
            foreach (var entry in preset.Monitors) { entry.Name ??= string.Empty; entry.Id ??= string.Empty; }
        }
        data.MonitorLastPreset ??= new Dictionary<string, string>();
        data.WarmLight ??= new WarmLightSettings();
        return data;
    }

    /// <summary>
    /// Takes display-settings.json from disk as it is now (an import wrote it). The two preset lists keep their identity (the lists' rules
    /// hold them), so they are emptied and filled; the warm-light settings and the rest are copied into the objects held (no file: the defaults).
    /// </summary>
    public bool ReloadFromDisk()
    {
        var fresh = Tidy(AppDataStore.Load<DisplaySettings>(Store) ?? new DisplaySettings());
        var (presets, monitors) = (Data.Presets, Data.MonitorPresets);
        SettingsCopy.Into(Data, fresh);
        presets.Clear();
        presets.AddRange(fresh.Presets);
        monitors.Clear();
        monitors.AddRange(fresh.MonitorPresets);
        (Data.Presets, Data.MonitorPresets) = (presets, monitors);
        _pictures.NotifyChanged();
        Monitors.NotifyChanged();
        return true;
    }

    /// <summary>The store as the program uses it: read from and written to the user's folder.</summary>
    public static DisplaySettingsStore OnDisk() =>
        new(AppDataStore.Load<DisplaySettings>(Store), settings => AppDataStore.Save(Store, settings));

    public DisplaySettings Data { get; }

    public WarmLightSettings WarmLight => Data.WarmLight;

    /// <summary>The picture preset list changed (raised on the thread that changed it, the UI thread).</summary>
    public event Action? PresetsChanged
    {
        add => _pictures.Changed += value;
        remove => _pictures.Changed -= value;
    }

    public IReadOnlyList<DisplayPreset> Presets => _pictures.Items;

    /// <summary>The picture presets (the rules are <see cref="PresetList{T}"/>'s: save, rename, move, delete with undo).</summary>
    public PresetList<DisplayPreset> Pictures => _pictures;

    public DisplayPreset? Find(string name) => _pictures.Find(name);

    /// <summary>Saves the warm-light settings after a change.</summary>
    public void SaveWarmLight() => _save(Data);

    /// <summary>Keeps the picture preset: one of the same name (any case) is replaced where it stands, else it goes last.</summary>
    public void Save(DisplayPreset preset) => _pictures.Save(preset);

    // ---------------------------------------------------------------- monitor presets

    /// <summary>The monitor presets (hardware values per monitor); renaming or deleting one keeps the per-monitor memory true.</summary>
    public PresetList<MonitorPreset> Monitors { get; }

    /// <summary>The monitor preset last applied to the monitor of this model name, if it still exists.</summary>
    public string? LastMonitorPreset(string monitorName) =>
        Data.MonitorLastPreset.TryGetValue(monitorName, out var name) && Monitors.Find(name) is { } preset ? preset.Name : null;

    /// <summary>Remembers that this preset was applied to the monitors of these model names, and saves.</summary>
    public void RememberMonitorPreset(string presetName, IEnumerable<string> monitorNames)
    {
        foreach (var monitor in monitorNames) Data.MonitorLastPreset[monitor] = presetName;
        _save(Data);
    }

    private void ForgetRenamed(string from, string to)
    {
        foreach (var key in Data.MonitorLastPreset.Where(p => PresetRules.Same(p.Value, from)).Select(p => p.Key).ToList())
            Data.MonitorLastPreset[key] = to;
    }

    private void ForgetDeleted(string name) =>
        Data.MonitorLastPreset = Data.MonitorLastPreset.Where(p => !PresetRules.Same(p.Value, name)).ToDictionary(p => p.Key, p => p.Value);
}
