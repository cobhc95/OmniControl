using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// Which real speakers the master plays on in Full control, where Windows' default playback device is a virtual cable (the master
/// bus's). Pure: the one rule the route planner and the default-device keeper share.
/// </summary>
internal static class MasterDevice
{
    /// <param name="Id">The device, or null when no real playback device is plugged in.</param>
    /// <param name="FellBack">The remembered device is not plugged in, so another one was taken instead.</param>
    public readonly record struct Choice(string? Id, bool FellBack);

    /// <summary>
    /// The default device when it is a real one (a choice made in Windows' Sound settings counts as the master's new device), else the
    /// remembered one when it is plugged in, else the first real playback device. A virtual cable is never a real device.
    /// </summary>
    public static Choice Real(IReadOnlyList<AudioDevice> playback, string? defaultId, string remembered)
    {
        bool IsReal(string? id) => id is not null && playback.Any(d => d.Id == id && !VirtualCables.IsCable(d.Name));
        if (IsReal(defaultId)) return new Choice(defaultId, false);
        if (IsReal(remembered)) return new Choice(remembered, false);
        return new Choice(playback.FirstOrDefault(d => !VirtualCables.IsCable(d.Name))?.Id, remembered.Length > 0);
    }
}
