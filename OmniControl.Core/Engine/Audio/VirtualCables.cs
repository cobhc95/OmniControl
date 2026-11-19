namespace OmniControl.Core.Engine;

/// A virtual audio cable: whatever is played into its playback side comes out of its recording side.
/// <param name="Name">Its short name as Windows shows it without the side ("CABLE", "CABLE-A", "Hi-Fi Cable", "Line 1").</param>
/// <param name="PlaybackId">Endpoint id of its playback side ("CABLE Input"): what is sent to the cable.</param>
/// <param name="RecordingId">Endpoint id of its recording side ("CABLE Output"), or null when Windows shows none (disabled).</param>
/// <param name="RecordingName">The recording side's name, the one apps list as a microphone.</param>
public sealed record VirtualCable(string Name, string PlaybackId, string? RecordingId, string? RecordingName)
{
    /// The recording side this playback side's sound comes out of: its own, or, for an extra input of a driver that has only one
    /// recording side (VB-CABLE's "CABLE In 16ch" next to "CABLE Input"), that driver's one recording side. Inputs with the same pair
    /// are one cable pair: sound sent to any of them reaches the same recording side. Null = no recording side known.
    public string? PairId { get; init; }
}

/// One cable pair: its recording side and every playback side whose sound comes out there (see <see cref="VirtualCable.PairId"/>).
public sealed record CablePair(string? RecordingId, IReadOnlyList<string> PlaybackIds);

/// What a virtual device is: a passive cable OmniControl can route through, or a virtual device of another kind it only lists.
public enum CableKind
{
    /// A passive cable: what is played into it comes out unchanged (VB-CABLE, Virtual Audio Cable).
    Cable,
    /// A virtual mixer's inputs and outputs (Voicemeeter): sound sent there goes into that mixer, it does not simply come back out.
    Mixer,
    /// A voice changer's device (Voicemod): its microphone gives the changed voice, not what was played.
    VoiceChanger,
}

/// Which ends of a virtual device Windows shows now.
public enum CableEnds { Both, InputOnly, OutputOnly, Missing }

/// A cable pair marked by the user: a playback device and a recording device that OmniControl does not recognise but that the user
/// says carry the same sound. Stored in the mixer settings; its <see cref="RecordingId"/> is its id, as for a recognised pair.
public sealed class CableMark
{
    public string Name { get; set; } = string.Empty;
    public string PlaybackId { get; set; } = string.Empty;
    public string RecordingId { get; set; } = string.Empty;
}

/// One virtual device as the Wiring tab lists it: a recognised or user-marked cable pair (with every input of the pair), a cable end
/// with no other end, or a virtual device of another kind. Only a <see cref="IsPair"/> can be used.
/// <param name="Id">Stable id for the settings: the recording side its sound comes out of (a pair), else an endpoint or driver id.</param>
/// <param name="Maker">Who makes it ("VB-Audio", "Eugene Muzychenko"), or empty for one the user marked.</param>
/// <param name="Inputs">The pair's playback sides (the usable cables); empty unless <see cref="IsPair"/>.</param>
public sealed record FoundCable(string Id, string Name, string Maker, CableKind Kind, CableEnds Ends, IReadOnlyList<VirtualCable> Inputs, bool Marked)
{
    /// A whole cable pair: both ends of a passive cable, the only kind OmniControl routes through.
    public bool IsPair => Kind == CableKind.Cable && Ends == CableEnds.Both;

    public bool Equals(FoundCable? other) =>
        other is not null && Id == other.Id && Name == other.Name && Maker == other.Maker && Kind == other.Kind && Ends == other.Ends
        && Marked == other.Marked && Inputs.SequenceEqual(other.Inputs);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Kind, Ends, Inputs.Count);
}

/// Finds the virtual cables among the devices Windows has (reads only).
public static class VirtualCables
{
    /// A known maker's driver: the words in brackets after its devices' names, who makes it and what it is.
    private sealed record Driver(string Match, string Maker, CableKind Kind);

    /// The one list of known virtual devices: a device whose name contains <see cref="Driver.Match"/> (case ignored) is that driver's.
    /// Unknown devices are never guessed (the user can mark a pair by hand, <see cref="CableMark"/>). Device names as Windows shows them
    /// (VB-Audio's pages and forum, Muzychenko's VAC manual "Endpoint names", user reports of Voicemod):
    /// <list type="bullet">
    /// <item>VB-CABLE: "CABLE Input (VB-Audio Virtual Cable)", "CABLE In 16ch (VB-Audio Virtual Cable)", "CABLE Output (VB-Audio Virtual Cable)".</item>
    /// <item>VB-CABLE A+B, C+D: "CABLE-A Input (VB-Audio Cable A)", "CABLE-A Output (VB-Audio Cable A)" (B, C, D alike).</item>
    /// <item>VB-Audio Hi-Fi Cable: "Hi-Fi Cable Input (VB-Audio Hi-Fi Cable)", "Hi-Fi Cable Output (VB-Audio Hi-Fi Cable)".</item>
    /// <item>Virtual Audio Cable (Eugene Muzychenko): "Line 1 (Virtual Audio Cable)" on both sides ("XXX N (Virtual Audio Cable)").</item>
    /// <item>Voicemeeter (VB-Audio): "Voicemeeter Input (VB-Audio Voicemeeter VAIO)", "Voicemeeter Output (...)", the AUX and VAIO3
    /// drivers alike (spelled "VoiceMeeter" by older versions); its mixer, not a cable.</item>
    /// <item>Voicemod: "Microphone (Voicemod Virtual Audio Device (WDM))"; a voice changer, not a cable.</item>
    /// </list>
    /// Order matters: the first match wins ("VB-Audio Cable" is VB-CABLE A to D, never "VB-Audio Virtual Cable").
    private static readonly Driver[] Drivers =
    {
        new("VB-Audio Virtual Cable", "VB-Audio", CableKind.Cable),
        new("VB-Audio Hi-Fi Cable", "VB-Audio", CableKind.Cable),
        new("VB-Audio Cable", "VB-Audio", CableKind.Cable),
        new("Virtual Audio Cable", "Eugene Muzychenko", CableKind.Cable),
        new("Voicemeeter", "VB-Audio", CableKind.Mixer),
        new("Voicemod Virtual Audio Device", "Voicemod", CableKind.VoiceChanger),
    };

    private const string PlaybackWord = " Input", RecordingWord = " Output";

    private static Driver? DriverFor(string deviceName) => Drivers.FirstOrDefault(d => deviceName.Contains(d.Match, StringComparison.OrdinalIgnoreCase));

    /// The device is one end of a known passive cable (never a real speaker or microphone, never a virtual mixer).
    public static bool IsCable(string deviceName) => DriverFor(deviceName)?.Kind == CableKind.Cable;

    /// The usable cables of every recognised pair, nothing forbidden, none marked (what a PC offers with no user choices).
    public static IReadOnlyList<VirtualCable> Find(IEnumerable<AudioDevice> playback, IReadOnlyList<AudioDevice> recording) =>
        Usable(Scan(playback.ToList(), recording, Array.Empty<CableMark>()), new HashSet<string>());

    /// The cables OmniControl may give out: every input of each whole pair (<see cref="FoundCable.IsPair"/>) the user has not switched
    /// off, in name order (the master bus takes the first free one).
    public static IReadOnlyList<VirtualCable> Usable(IReadOnlyList<FoundCable> found, IReadOnlySet<string> forbidden) =>
        found.Where(f => f.IsPair && !forbidden.Contains(f.Id))
            .SelectMany(f => f.Inputs)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// Every virtual device among <paramref name="playback"/> and <paramref name="recording"/>: the recognised cable pairs (a playback
    /// side and the recording side of the same driver that carries its sound; extra inputs join their pair), the user's marked pairs,
    /// cable ends with no other end, and virtual devices of other kinds, one entry each.
    public static IReadOnlyList<FoundCable> Scan(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording, IReadOnlyList<CableMark> marks)
    {
        var found = new List<FoundCable>();
        var cables = CablesOf(playback, recording);
        foreach (var group in cables.GroupBy(c => c.PairId ?? c.PlaybackId))
        {
            var main = group.FirstOrDefault(c => c.RecordingId is not null) ?? group.First();
            var pairId = group.First().PairId;
            found.Add(new FoundCable(group.Key, main.Name, DriverFor(DeviceName(playback, main.PlaybackId))!.Maker, CableKind.Cable,
                pairId is null ? CableEnds.InputOnly : CableEnds.Both, pairId is null ? Array.Empty<VirtualCable>() : group.ToList(), false));
        }
        var paired = cables.Select(c => c.PairId).OfType<string>().ToHashSet();
        foreach (var r in recording.Where(r => IsCable(r.Name) && !paired.Contains(r.Id)))
            found.Add(new FoundCable(r.Id, ShortName(r.Name, RecordingWord), DriverFor(r.Name)!.Maker, CableKind.Cable, CableEnds.OutputOnly, Array.Empty<VirtualCable>(), false));
        found.AddRange(Marked(playback, recording, marks, found));
        found.AddRange(OtherKinds(playback, recording));
        return found.OrderBy(f => f.Kind).ThenBy(f => f.Ends).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// Every passive cable input among <paramref name="playback"/>, paired with its recording side by name.
    private static List<VirtualCable> CablesOf(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording) =>
        playback.Where(d => IsCable(d.Name))
            .Select(d =>
            {
                // VB-Audio names the two sides "<cable> Input (...)" and "<cable> Output (...)"; Virtual Audio Cable uses one name for both.
                var other = recording.FirstOrDefault(r => r.Name.Equals(d.Name.Replace(PlaybackWord, RecordingWord), StringComparison.OrdinalIgnoreCase))
                    ?? recording.FirstOrDefault(r => r.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));
                return new VirtualCable(ShortName(d.Name, PlaybackWord), d.Id, other?.Id, other?.Name) { PairId = other?.Id ?? OnlyRecordingOf(d.Name, recording) };
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// The user's marked pairs; a mark whose devices a recognised cable already has is left out (it is that cable).
    private static IEnumerable<FoundCable> Marked(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording,
        IReadOnlyList<CableMark> marks, IReadOnlyList<FoundCable> recognised)
    {
        var taken = recognised.SelectMany(f => f.Inputs.Select(c => c.PlaybackId).Append(f.Id)).ToHashSet();
        foreach (var mark in marks.Where(m => !taken.Contains(m.PlaybackId) && !taken.Contains(m.RecordingId)))
        {
            var p = playback.FirstOrDefault(d => d.Id == mark.PlaybackId);
            var r = recording.FirstOrDefault(d => d.Id == mark.RecordingId);
            var ends = (p, r) switch
            {
                (not null, not null) => CableEnds.Both,
                (not null, null) => CableEnds.InputOnly,
                (null, not null) => CableEnds.OutputOnly,
                _ => CableEnds.Missing,
            };
            var inputs = p is not null && r is not null
                ? new[] { new VirtualCable(mark.Name, p.Id, r.Id, r.Name) { PairId = r.Id } }
                : Array.Empty<VirtualCable>();
            yield return new FoundCable(mark.RecordingId, mark.Name, string.Empty, CableKind.Cable, ends, inputs, true);
        }
    }

    /// Virtual devices that are not cables (Voicemeeter, Voicemod): one entry per driver, never used.
    private static IEnumerable<FoundCable> OtherKinds(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording)
    {
        var devices = playback.Select(d => (d, Playback: true)).Concat(recording.Select(d => (d, Playback: false)))
            .Where(x => DriverFor(x.d.Name) is { Kind: not CableKind.Cable });
        foreach (var group in devices.GroupBy(x => DriverOf(x.d.Name) ?? x.d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var driver = DriverFor(group.First().d.Name)!;
            var hasIn = group.Any(x => x.Playback);
            var hasOut = group.Any(x => !x.Playback);
            var ends = hasIn && hasOut ? CableEnds.Both : hasIn ? CableEnds.InputOnly : CableEnds.OutputOnly;
            yield return new FoundCable(group.Key, group.Key.Trim('(', ')'), driver.Maker, driver.Kind, ends, Array.Empty<VirtualCable>(), false);
        }
    }

    /// The cable pairs of <paramref name="cables"/>, in the cables' order (a cable with no known recording side is a pair of its own).
    public static IReadOnlyList<CablePair> Pairs(IReadOnlyList<VirtualCable> cables) =>
        cables.GroupBy(c => c.PairId ?? c.PlaybackId)
            .Select(g => new CablePair(g.First().PairId, g.Select(c => c.PlaybackId).ToList()))
            .ToList();

    /// The one recording side of the driver named in brackets after <paramref name="playbackName"/> ("(VB-Audio Virtual Cable)"), when
    /// that driver has exactly one; null otherwise (several lines, each its own pair, or none).
    private static string? OnlyRecordingOf(string playbackName, IReadOnlyList<AudioDevice> recording)
    {
        if (DriverOf(playbackName) is not { } driver) return null;
        var sides = recording.Where(r => IsCable(r.Name) && string.Equals(DriverOf(r.Name), driver, StringComparison.OrdinalIgnoreCase)).ToList();
        return sides.Count == 1 ? sides[0].Id : null;
    }

    private static string DeviceName(IReadOnlyList<AudioDevice> devices, string id) => devices.First(d => d.Id == id).Name;

    /// "CABLE In 16ch (VB-Audio Virtual Cable)" -> "(VB-Audio Virtual Cable)"; null without brackets.
    private static string? DriverOf(string deviceName)
    {
        var at = deviceName.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? deviceName[(at + 1)..] : null;
    }

    /// "CABLE-A Input (VB-Audio Cable A)" -> "CABLE-A" (with <paramref name="side"/> " Input").
    private static string ShortName(string deviceName, string side)
    {
        var at = deviceName.IndexOf(" (", StringComparison.Ordinal);
        var name = at > 0 ? deviceName[..at] : deviceName;
        return name.EndsWith(side, StringComparison.OrdinalIgnoreCase) ? name[..^side.Length] : name;
    }
}
