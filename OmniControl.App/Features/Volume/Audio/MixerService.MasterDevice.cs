namespace OmniControl.App.Features.Volume.Audio;

/// Which device the master (its volume, mute, balance, meter and pop-up) acts on. Normally Windows' default playback device; in Full
/// control, while a virtual cable is the default (the master bus), the real device the bus plays on. The effects engine
/// (<see cref="Engine.BusDefault"/>) tells the mixer; everything that touches the master asks <see cref="MasterDeviceId"/>.
public sealed partial class MixerService
{
    /// Windows' default playback device now (as of the last scan); null when there is none.
    public string? DefaultDeviceId => Devices.FirstOrDefault(d => d.IsDefault)?.Id;

    /// The device the master acts on (endpoint id), or null = Windows' default playback device.
    public string? MasterDeviceId { get; private set; }

    /// The master's device changed (UI thread): its level, pop-up watch and meter follow.
    public event Action? MasterDeviceChanged;

    /// Whether OmniControl made a virtual cable Windows' default device, so the master's real device is chosen apart from it.
    public bool MasterBusOwnsDefault => Settings.BusCable.Length > 0;

    internal void SetMasterDevice(string? deviceId)
    {
        if (MasterDeviceId == deviceId) return;
        MasterDeviceId = deviceId;
        MasterDeviceChanged?.Invoke();
    }

    /// The real device the master bus plays on (the Wiring tab's choice); Windows' default stays the cable.
    public void SetRealMasterDevice(string deviceId)
    {
        if (Settings.RealMasterDevice == deviceId) return;
        Settings.RealMasterDevice = deviceId;
        Changed();
    }

    /// Changes Windows' default playback device without reporting or scanning (the engine's own change, which reports in its own words).
    internal bool TrySetDefaultDevice(string deviceId, out string error) => CoreAudio.TrySetDefaultDevice(deviceId, out error);

    /// The loudest level now playing on a playback device (what is sent to it), for a device that is not the master's.
    internal bool DevicePeak(string deviceId, out float peak)
    {
        var ok = _meters.Peaks(deviceId, false, out var left, out var right);
        peak = Math.Max(left, right);
        return ok;
    }

    /// Looks at the playback devices again (the default device may have changed since the last scan).
    internal void RefreshDevices() => Devices = CoreAudio.Devices();

    /// A playback device's master level and mute (the cable mirror's reads); false when the device is gone.
    internal bool ReadLevel(string deviceId, out int percent, out bool muted) => CoreAudio.GetMaster(out percent, out muted, deviceId);

    /// Sets a playback device's master level and mute (the cable mirror's writes).
    internal void WriteLevel(string deviceId, int percent, bool muted)
    {
        CoreAudio.SetMasterVolume(percent, deviceId);
        CoreAudio.SetMasterMute(muted, deviceId);
    }
}
