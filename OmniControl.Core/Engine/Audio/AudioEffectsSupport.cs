using OmniControl.Core.Engine.Apo;

namespace OmniControl.Core.Engine;

/// What this PC offers the effects engine, as found by <see cref="IAudioEffectsSupport.Check"/>.
/// <param name="ProcessLoopback">Windows can record one program's sound on its own (process loopback capture).</param>
/// <param name="Cables">The virtual cables OmniControl may use (<see cref="VirtualCables.Usable"/>), in name order; empty when there is none.</param>
/// <param name="Apo">What is in place for Equalizer APO (the master's and a cable's effects).</param>
/// <param name="DefaultDeviceId">The default playback device; null when there is none.</param>
public sealed record AudioEffectsSupport(bool ProcessLoopback, IReadOnlyList<VirtualCable> Cables, ApoState Apo, string? DefaultDeviceId)
{
    /// Every virtual device found (<see cref="VirtualCables.Scan"/>), usable or not, for the Wiring tab's Virtual cables list.
    public IReadOnlyList<FoundCable> Found { get; init; } = Array.Empty<FoundCable>();

    /// Record equality compares the cable lists by content, so a re-check that found the same cables raises no change.
    public bool Equals(AudioEffectsSupport? other) =>
        other is not null && ProcessLoopback == other.ProcessLoopback && Cables.SequenceEqual(other.Cables) && Found.SequenceEqual(other.Found)
        && Apo == other.Apo && DefaultDeviceId == other.DefaultDeviceId;

    public override int GetHashCode() => HashCode.Combine(ProcessLoopback, Cables.Count, Found.Count, Apo, DefaultDeviceId);
}

/// Looks at what this PC offers for sound effects. Reads only: nothing is installed or changed.
public interface IAudioEffectsSupport
{
    /// A few cheap Windows look-ups (version, device names, registry keys); call it off the UI thread. <paramref name="marks"/> are the
    /// pairs the user marked as cables; every pair found is in <see cref="AudioEffectsSupport.Cables"/> (the caller leaves out the ones
    /// the user switched off).
    AudioEffectsSupport Check(IReadOnlyList<CableMark> marks);
}

/// <inheritdoc />
public sealed class AudioEffectsProbe : IAudioEffectsSupport
{
    /// Process loopback capture arrived with Windows 10 version 2004 (build 19041; NAudio and community projects use it from
    /// there, Microsoft's reference page names build 20348 as the documented minimum).
    public const int ProcessLoopbackBuild = 19041;

    public AudioEffectsSupport Check(IReadOnlyList<CableMark> marks)
    {
        var devices = CoreAudio.Devices();
        var inputs = CoreAudio.InputDevices();
        var defaultId = devices.FirstOrDefault(d => d.IsDefault)?.Id;
        var found = VirtualCables.Scan(devices, inputs, marks);
        var cableEnds = found.Where(f => f.IsPair).Select(f => f.Id).ToHashSet();
        var microphones = inputs.Where(d => !VirtualCables.IsCable(d.Name) && !cableEnds.Contains(d.Id)).Select(d => d.Id);
        return new AudioEffectsSupport(Environment.OSVersion.Version.Build >= ProcessLoopbackBuild,
            VirtualCables.Usable(found, new HashSet<string>()), ApoStatus.Read(defaultId, devices.Select(d => d.Id), microphones), defaultId) { Found = found };
    }
}
