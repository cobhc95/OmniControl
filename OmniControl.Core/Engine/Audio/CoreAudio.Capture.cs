using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>Recording devices (microphones and other inputs): list, volume and mute.</summary>
public static partial class CoreAudio
{
    /// <summary>Recording devices that are plugged in and enabled right now.</summary>
    public static IReadOnlyList<AudioDevice> InputDevices()
    {
        var result = new List<AudioDevice>();
        try
        {
            var e = NewEnumerator();
            var defaultId = e.GetDefaultAudioEndpoint(Capture, Multimedia, out var def) == 0 ? DeviceId(def) : string.Empty;
            foreach (var d in ActiveDevices(e, Capture))
            {
                var id = DeviceId(d);
                result.Add(new AudioDevice(id, DeviceName(d), id == defaultId));
            }
        }
        catch { /* no audio service */ }
        return result;
    }

    private static IAudioEndpointVolume? EndpointVolume(string id)
    {
        try
        {
            if (NewEnumerator().GetDevice(id, out var dev) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) == 0 ? o as IAudioEndpointVolume : null;
        }
        catch { return null; }
    }

    public static bool GetDeviceVolume(string id, out int percent, out bool muted)
    {
        percent = 0; muted = false;
        var v = EndpointVolume(id);
        if (v is null || v.GetMasterVolumeLevelScalar(out var level) != 0) return false;
        percent = (int)Math.Round(level * 100);
        if (v.GetMute(out var m) == 0) muted = m;
        return true;
    }

    public static bool SetDeviceVolume(string id, int percent) =>
        EndpointVolume(id) is { } v && v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref _ctx) == 0;

    public static bool SetDeviceMute(string id, bool mute) => EndpointVolume(id) is { } v && v.SetMute(mute, ref _ctx) == 0;
}
