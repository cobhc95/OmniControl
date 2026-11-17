namespace OmniControl.App.Audio;

/// <summary>
/// What a preset holds: an equaliser's bands, or a built-in effect's parameter values (the compressor's), never both. Plain data, saved
/// in presets.json and, as <see cref="FxSlot.Custom"/>, with an effect.
/// </summary>
public sealed class PresetContent
{
    public List<EqBand>? Bands { get; set; }
    public List<double>? Values { get; set; }

    public PresetContent Copy() => new() { Bands = Bands?.Select(b => b.Copy()).ToList(), Values = Values?.ToList() };

    /// <summary>The same sound: bands equal ids aside, or the same values.</summary>
    public bool Matches(PresetContent? other) =>
        other is not null && SameBands(Bands, other.Bands) && SameValues(Values, other.Values);

    private static bool SameBands(List<EqBand>? a, List<EqBand>? b) =>
        a is null || b is null ? a is null && b is null
            : a.Count == b.Count && a.Zip(b).All(x => x.First.Type == x.Second.Type && x.First.Freq == x.Second.Freq && x.First.Q == x.Second.Q
                && x.First.Gain == x.Second.Gain && x.First.Enabled == x.Second.Enabled);

    private static bool SameValues(List<double>? a, List<double>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);
}

/// <summary>One preset of the user's own: its effect kind (<see cref="FxKind.Kind"/>), its name and what it holds.</summary>
public sealed class UserPreset
{
    /// <summary>Stable id given by <see cref="UserPresetStore"/> (null in an export file and in a file saved before presets had ids).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PresetContent Content { get; set; } = new();
    public DateTime Saved { get; set; } = DateTime.Now;
}

/// <summary>
/// A device folder as a preset (docs/mockups/fx-device-folders.html "Save folder as preset"): the folder's device, its Mix and its
/// effects with all their values. Kept beside the effect presets in <see cref="UserPresetStore"/>; loading one into a folder replaces
/// that folder's effects and Mix, loading it with no folder makes a folder for its device.
/// </summary>
public sealed class FolderPreset
{
    /// <summary>Stable id given by <see cref="UserPresetStore"/> (a shortcut follows it).</summary>
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The folder's playback device (endpoint id) and its name, kept for when the device is not plugged in.</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The folder's Mix, 0-100 percent.</summary>
    public int Mix { get; set; } = FxSlot.DefaultWet;

    public List<FxSlot> Effects { get; set; } = new();

    public DateTime Saved { get; set; } = DateTime.Now;

    /// <summary>The same preset with its own copies of every effect (a slot is never shared between a chain and a preset).</summary>
    public FolderPreset Copy() => new()
    {
        Id = Id, Name = Name, DeviceId = DeviceId, DeviceName = DeviceName, Mix = Mix, Saved = Saved,
        Effects = Effects.Select(e => e.Copy()).ToList(),
    };

    /// <summary>The same sound: the same device, Mix and effects with the same settings (compared as saved).</summary>
    public bool Matches(FolderPreset? other) =>
        other is not null && string.Equals(DeviceId, other.DeviceId, StringComparison.OrdinalIgnoreCase) && Mix == other.Mix
        && Effects.Count == other.Effects.Count && Effects.Zip(other.Effects).All(x => SameEffect(x.First, x.Second));

    private static bool SameEffect(FxSlot a, FxSlot b) =>
        a.Kind == b.Kind && a.Enabled == b.Enabled && a.Wet == b.Wet && a.Path == b.Path && a.State == b.State && a.Values.SequenceEqual(b.Values)
        && new PresetContent { Bands = a.Bands }.Matches(new PresetContent { Bands = b.Bands });
}
