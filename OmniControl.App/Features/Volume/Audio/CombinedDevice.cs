namespace OmniControl.App.Features.Volume.Audio;

/// What a combined device joins (stored numbers are fixed).
public enum CombinedKind
{
    /// Several playback devices that play the same sound.
    Speakers = 0,
    /// Several recording devices mixed into one microphone.
    Microphones = 1,
}

/// A device the user made by combining others. It owns one virtual cable: for speakers, whatever is sent to the cable
/// plays on every member; for microphones, the members are recorded, each at its own level, mixed and played into the
/// cable, whose recording side apps use as their microphone. Kept in mixer.json even while members are unplugged.
public sealed class CombinedDevice
{
    /// A member's level before it is changed (as recorded).
    public const int DefaultMemberLevel = Core.Models.Percent.Max;
    /// The prefix of a combined device's key (as a "plays on" target, an FX chain key and an engine row key).
    public const string KeyPrefix = "combo:";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public CombinedKind Kind { get; set; }

    /// Endpoint ids of the member devices, in the order the user ticked them.
    public List<string> Members { get; set; } = new();

    /// The cable it last used (its playback side's endpoint id), so apps that picked it keep finding it; empty = none yet.
    public string Cable { get; set; } = string.Empty;

    /// A combined microphone's effects (switched on, chain), level and mute; unused for speakers.
    public bool FxOn { get; set; }
    /// The user switched the effects off themselves, so adding an effect does not switch them back on (<see cref="MixerService.EffectsAdded"/>).
    public bool FxUserOff { get; set; }
    public List<FxSlot> Fx { get; set; } = new();
    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }

    /// A combined microphone's member levels in percent (0-<see cref="Core.Models.Percent.BoostMax"/>) by endpoint id; a member that is
    /// not listed is at <see cref="DefaultMemberLevel"/>. Kept beside <see cref="Members"/> so older files load unchanged.
    public Dictionary<string, int> MemberLevel { get; set; } = new();

    /// A combined microphone's muted members (endpoint ids).
    public HashSet<string> MemberMuted { get; set; } = new();

    public string Key => KeyPrefix + Id;

    public int LevelOf(string member) => MemberLevel.GetValueOrDefault(member, DefaultMemberLevel);

    public bool IsMuted(string member) => MemberMuted.Contains(member);

    /// What the mix multiplies a member's sound by (0 while muted).
    public float GainOf(string member) => IsMuted(member) ? 0f : Core.Models.Percent.ToGain(LevelOf(member));

    /// Forgets the levels and mutes of devices that are no longer members.
    public void DropStaleMemberSettings()
    {
        foreach (var gone in MemberLevel.Keys.Where(k => !Members.Contains(k)).ToList()) MemberLevel.Remove(gone);
        MemberMuted.RemoveWhere(m => !Members.Contains(m));
    }

    public static bool IsKey(string? key) => key is not null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// The devices that can be members (of either kind): never a virtual cable, so a combined device never contains itself or another one's cable.
    public static IEnumerable<Core.Engine.AudioDevice> Candidates(IEnumerable<Core.Engine.AudioDevice> devices) =>
        devices.Where(d => !Core.Engine.VirtualCables.IsCable(d.Name));
}
