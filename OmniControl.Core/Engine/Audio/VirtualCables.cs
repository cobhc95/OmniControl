namespace OmniControl.Core.Engine;

/// <summary>
/// A virtual audio cable: whatever is played into its playback side comes out of its recording side.
/// </summary>
/// <param name="Name">Its short name as Windows shows it without the side ("CABLE", "CABLE-A", "Hi-Fi Cable", "Line 1").</param>
/// <param name="PlaybackId">Endpoint id of its playback side ("CABLE Input"): what is sent to the cable.</param>
/// <param name="RecordingId">Endpoint id of its recording side ("CABLE Output"), or null when Windows shows none (disabled).</param>
/// <param name="RecordingName">The recording side's name, the one apps list as a microphone.</param>
public sealed record VirtualCable(string Name, string PlaybackId, string? RecordingId, string? RecordingName);

/// <summary>Finds the virtual cables among the devices Windows has (reads only).</summary>
public static class VirtualCables
{
    /// <summary>
    /// The one list of known passive cables: a playback device whose name contains one of these (case ignored) is a cable. VB-Audio's
    /// cables ("CABLE Input (VB-Audio Virtual Cable)", "CABLE-A Input (VB-Audio Cable A)", "Hi-Fi Cable Input (VB-Audio Hi-Fi Cable)")
    /// and Virtual Audio Cable's lines ("Line 1 (Virtual Audio Cable)"). Voicemeeter's inputs are not listed: they feed Voicemeeter's
    /// own mixer, so sound sent there does not simply come back out.
    /// </summary>
    private static readonly string[] Drivers = { "VB-Audio Virtual Cable", "VB-Audio Hi-Fi Cable", "VB-Audio Cable", "Virtual Audio Cable" };

    private const string PlaybackWord = " Input", RecordingWord = " Output";

    public static bool IsCable(string deviceName) => Drivers.Any(d => deviceName.Contains(d, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every cable among <paramref name="playback"/>, paired with its recording side from <paramref name="recording"/>, by name.</summary>
    public static IReadOnlyList<VirtualCable> Find(IEnumerable<AudioDevice> playback, IReadOnlyList<AudioDevice> recording) =>
        playback.Where(d => IsCable(d.Name))
            .Select(d =>
            {
                // VB-Audio names the two sides "<cable> Input (...)" and "<cable> Output (...)"; Virtual Audio Cable uses one name for both.
                var other = recording.FirstOrDefault(r => r.Name.Equals(d.Name.Replace(PlaybackWord, RecordingWord), StringComparison.OrdinalIgnoreCase))
                    ?? recording.FirstOrDefault(r => r.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));
                return new VirtualCable(ShortName(d.Name), d.Id, other?.Id, other?.Name);
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>"CABLE-A Input (VB-Audio Cable A)" -> "CABLE-A".</summary>
    private static string ShortName(string deviceName)
    {
        var at = deviceName.IndexOf(" (", StringComparison.Ordinal);
        var name = at > 0 ? deviceName[..at] : deviceName;
        return name.EndsWith(PlaybackWord, StringComparison.OrdinalIgnoreCase) ? name[..^PlaybackWord.Length] : name;
    }
}
