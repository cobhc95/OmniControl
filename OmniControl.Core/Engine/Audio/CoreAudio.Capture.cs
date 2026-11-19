namespace OmniControl.Core.Engine;

/// Recording devices (microphones and other inputs): list, volume and mute.
public static partial class CoreAudio
{
    /// Recording devices that are plugged in and enabled right now.
    public static IReadOnlyList<AudioDevice> InputDevices() => DeviceList(Capture);

    private static IAudioEndpointVolume? EndpointVolume(string id)
    {
        try
        {
            if (NewEnumerator().GetDevice(id, out var dev) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) == 0 ? o as IAudioEndpointVolume : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; /* device vanished or has no volume interface */ }
    }

    public static bool GetDeviceVolume(string id, out int percent, out bool muted)
    {
        percent = 0; muted = false;
        var v = EndpointVolume(id);
        if (v is null || v.GetMasterVolumeLevelScalar(out var level) != 0) return false;
        percent = Percent.FromScalar(level);
        if (v.GetMute(out var m) == 0) muted = m;
        return true;
    }

    public static bool SetDeviceVolume(string id, int percent) =>
        EndpointVolume(id) is { } v && v.SetMasterVolumeLevelScalar(Percent.ToScalar(percent), ref _ctx) == 0;

    public static bool SetDeviceMute(string id, bool mute) => EndpointVolume(id) is { } v && v.SetMute(mute, ref _ctx) == 0;
}
