using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// Which device the master (its volume, mute, balance, meter and pop-up) acts on. Normally Windows' default playback device; in Full
/// control, while a virtual cable is the default (the master bus), the real device the bus plays on. The effects engine
/// (<see cref="Engine.BusDefault"/>) tells the mixer; everything that touches the master asks <see cref="MasterDeviceId"/>.
/// </summary>
public sealed partial class MixerService
{
    /// <summary>Windows' default playback device now (as of the last scan); null when there is none.</summary>
    public string? DefaultDeviceId => Devices.FirstOrDefault(d => d.IsDefault)?.Id;

    /// <summary>The device the master acts on (endpoint id), or null = Windows' default playback device.</summary>
    public string? MasterDeviceId { get; private set; }

    /// <summary>The master's device changed (UI thread): its level, pop-up watch and meter follow.</summary>
    public event Action? MasterDeviceChanged;

    /// <summary>Whether OmniControl made a virtual cable Windows' default device, so the master's real device is chosen apart from it.</summary>
    public bool MasterBusOwnsDefault => Settings.BusCable.Length > 0;

    internal void SetMasterDevice(string? deviceId)
    {
        if (MasterDeviceId == deviceId) return;
        MasterDeviceId = deviceId;
        MasterDeviceChanged?.Invoke();
    }

    /// <summary>The real device the master bus plays on (the Wiring tab's choice); Windows' default stays the cable.</summary>
    public void SetRealMasterDevice(string deviceId)
    {
        if (Settings.RealMasterDevice == deviceId) return;
        Settings.RealMasterDevice = deviceId;
        Changed();
    }

    /// <summary>Changes Windows' default playback device without reporting or scanning (the engine's own change, which reports in its own words).</summary>
    internal bool TrySetDefaultDevice(string deviceId, out string error) => CoreAudio.TrySetDefaultDevice(deviceId, out error);

    /// <summary>The loudest level now playing on a playback device (what is sent to it), for a device that is not the master's.</summary>
    internal bool DevicePeak(string deviceId, out float peak)
    {
        var ok = CoreAudio.DevicePeaks(deviceId, false, out var left, out var right);
        peak = Math.Max(left, right);
        return ok;
    }

    /// <summary>Looks at the playback devices again (the default device may have changed since the last scan).</summary>
    internal void RefreshDevices() => Devices = CoreAudio.Devices();

    /// <summary>A playback device's master level and mute (the cable mirror's reads); false when the device is gone.</summary>
    internal bool ReadLevel(string deviceId, out int percent, out bool muted) => CoreAudio.GetMaster(out percent, out muted, deviceId);

    /// <summary>Sets a playback device's master level and mute (the cable mirror's writes).</summary>
    internal void WriteLevel(string deviceId, int percent, bool muted)
    {
        CoreAudio.SetMasterVolume(percent, deviceId);
        CoreAudio.SetMasterMute(muted, deviceId);
    }
}
