namespace OmniControl.App.Audio;

/// <summary>What a combined device joins (stored numbers are fixed).</summary>
public enum CombinedKind
{
    /// <summary>Several playback devices that play the same sound.</summary>
    Speakers = 0,
    /// <summary>Several recording devices mixed into one microphone.</summary>
    Microphones = 1,
}

/// <summary>
/// A device the user made by combining others (Full control). It owns one virtual cable: for speakers, whatever is sent to the cable
/// plays on every member; for microphones, the members are recorded, mixed and played into the cable, whose recording side apps use
/// as their microphone. Kept in mixer.json even while members are unplugged.
/// </summary>
public sealed class CombinedDevice
{
    /// <summary>The prefix of a combined device's key (as a "plays on" target, an FX chain key and an engine row key).</summary>
    public const string KeyPrefix = "combo:";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public CombinedKind Kind { get; set; }

    /// <summary>Endpoint ids of the member devices, in the order the user ticked them.</summary>
    public List<string> Members { get; set; } = new();

    /// <summary>The cable it last used (its playback side's endpoint id), so apps that picked it keep finding it; empty = none yet.</summary>
    public string Cable { get; set; } = string.Empty;

    /// <summary>A combined microphone's effects (switched on, chain), level and mute; unused for speakers.</summary>
    public bool FxOn { get; set; }
    public List<FxSlot> Fx { get; set; } = new();
    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }

    public string Key => KeyPrefix + Id;

    public static bool IsKey(string? key) => key is not null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);
}
