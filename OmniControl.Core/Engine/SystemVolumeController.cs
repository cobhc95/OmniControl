using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.Core.Engine;

/// <summary>System (master) volume of the default playback device, through <see cref="CoreAudio"/>.</summary>
public sealed class SystemVolumeController : ISystemVolume
{
    public OperationResult GetVolume(out int percent)
    {
        if (CoreAudio.GetMaster(out percent, out _)) return OperationResult.Ok($"System volume {percent}%");
        percent = 50;
        return OperationResult.Fail("Core Audio: no playback device");
    }

    public OperationResult SetVolume(int percent) =>
        CoreAudio.SetMasterVolume(percent)
            ? OperationResult.Ok($"System volume {percent}%")
            : OperationResult.Fail("Core Audio: couldn't set the volume");

    public OperationResult SetMute(bool mute) =>
        CoreAudio.SetMasterMute(mute)
            ? OperationResult.Ok(mute ? "Muted" : "Unmuted")
            : OperationResult.Fail("Core Audio: couldn't change mute");

    public OperationResult GetMute(out bool muted)
    {
        if (CoreAudio.GetMaster(out _, out muted)) return OperationResult.Ok(muted ? "Muted" : "Unmuted");
        muted = false;
        return OperationResult.Fail("Core Audio: no playback device");
    }
}
