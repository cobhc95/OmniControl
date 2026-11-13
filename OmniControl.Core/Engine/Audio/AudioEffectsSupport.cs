using OmniControl.Core.Engine.Apo;

namespace OmniControl.Core.Engine;

/// <summary>What this PC offers for the sound modes, as found by <see cref="IAudioEffectsSupport.Check"/>.</summary>
/// <param name="ProcessLoopback">Windows can record one program's sound on its own (process loopback capture).</param>
/// <param name="Cables">Every virtual audio cable found (<see cref="VirtualCables"/>), in name order; empty when there is none.</param>
/// <param name="Apo">What is in place for Equalizer APO / OmniControl APO (master and microphone effects in Fast mode).</param>
/// <param name="DefaultDeviceId">The default playback device; null when there is none.</param>
public sealed record AudioEffectsSupport(bool ProcessLoopback, IReadOnlyList<VirtualCable> Cables, ApoState Apo, string? DefaultDeviceId)
{
    /// <summary>Record equality compares the cable lists by content, so a re-check that found the same cables raises no change.</summary>
    public bool Equals(AudioEffectsSupport? other) =>
        other is not null && ProcessLoopback == other.ProcessLoopback && Cables.SequenceEqual(other.Cables) && Apo == other.Apo
        && DefaultDeviceId == other.DefaultDeviceId;

    public override int GetHashCode() => HashCode.Combine(ProcessLoopback, Cables.Count, Apo, DefaultDeviceId);
}

/// <summary>Looks at what this PC offers for sound effects. Reads only: nothing is installed or changed.</summary>
public interface IAudioEffectsSupport
{
    /// <summary>A few cheap Windows look-ups (version, device names, registry keys); call it off the UI thread.</summary>
    AudioEffectsSupport Check();
}

/// <inheritdoc />
public sealed class AudioEffectsProbe : IAudioEffectsSupport
{
    /// <summary>
    /// Process loopback capture arrived with Windows 10 version 2004 (build 19041; NAudio and community projects use it from
    /// there, Microsoft's reference page names build 20348 as the documented minimum).
    /// </summary>
    public const int ProcessLoopbackBuild = 19041;

    public AudioEffectsSupport Check()
    {
        var devices = CoreAudio.Devices();
        var inputs = CoreAudio.InputDevices();
        var defaultId = devices.FirstOrDefault(d => d.IsDefault)?.Id;
        var microphones = inputs.Where(d => !VirtualCables.IsCable(d.Name)).Select(d => d.Id);
        return new AudioEffectsSupport(Environment.OSVersion.Version.Build >= ProcessLoopbackBuild,
            VirtualCables.Find(devices, inputs), ApoStatus.Read(defaultId, microphones), defaultId);
    }
}
