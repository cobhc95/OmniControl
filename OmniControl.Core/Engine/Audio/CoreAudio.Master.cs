using OmniControl.Core.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>
/// Master volume of a playback device: the default one, or the one named by <c>deviceId</c> (what the master acts on while a virtual
/// cable is Windows' default device).
/// </summary>
public static partial class CoreAudio
{
    // ---- master volume ----

    private static IAudioEndpointVolume? Master(string? deviceId)
    {
        if (deviceId is not null) return EndpointVolume(deviceId);
        try
        {
            var e = NewEnumerator();
            if (e.GetDefaultAudioEndpoint(Render, Multimedia, out var dev) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) == 0 ? o as IAudioEndpointVolume : null;
        }
        catch { return null; /* no playback device */ }
    }

    public static string DefaultDeviceName()
    {
        try
        {
            return NewEnumerator().GetDefaultAudioEndpoint(Render, Multimedia, out var d) == 0 ? DeviceName(d) : "No playback device";
        }
        catch { return "No playback device"; }
    }

    public static bool GetMaster(out int percent, out bool muted, string? deviceId = null)
    {
        percent = 0; muted = false;
        var m = Master(deviceId);
        if (m is null || m.GetMasterVolumeLevelScalar(out var v) != 0) return false;
        percent = Percent.FromScalar(v);
        if (m.GetMute(out var mu) == 0) muted = mu;
        return true;
    }

    public static bool SetMasterVolume(int percent, string? deviceId = null) =>
        Master(deviceId) is { } m && m.SetMasterVolumeLevelScalar(Percent.ToScalar(percent), ref _ctx) == 0;

    public static bool SetMasterMute(bool mute, string? deviceId = null) => Master(deviceId) is { } m && m.SetMute(mute, ref _ctx) == 0;

    /// <summary>Left/right balance of the device (-100..100), read from its channel levels.</summary>
    public static bool GetMasterPan(out int pan, string? deviceId = null)
    {
        pan = 0;
        var m = Master(deviceId);
        if (m is null || m.GetChannelCount(out var n) != 0 || n < 2) return false;
        if (m.GetChannelVolumeLevelScalar(0, out var l) != 0 || m.GetChannelVolumeLevelScalar(1, out var r) != 0) return false;
        pan = PanMath.FromLevels(l, r);
        return true;
    }

    public static bool SetMasterPan(int pan, string? deviceId = null)
    {
        var m = Master(deviceId);
        if (m is null || m.GetChannelCount(out var n) != 0 || n < 2) return false;
        PanMath.ToLevels(pan, out var l, out var r);
        return m.SetChannelVolumeLevelScalar(0, l, ref _ctx) == 0 && m.SetChannelVolumeLevelScalar(1, r, ref _ctx) == 0;
    }

}
