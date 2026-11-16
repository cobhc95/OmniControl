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
