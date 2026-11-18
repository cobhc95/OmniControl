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
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; /* no playback device */ }
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

    /// <summary>
    /// How loud a loopback of the device is against its sound before Windows' volume, as a linear amplitude factor (0 to 1): the master
    /// level in dB turned into amplitude, since Windows scales the samples by that (the slider's percent is an audio taper, not the gain);
    /// 1 when the device applies the volume itself, after the loopback. The channel balance is left out. Null when it can't be read.
    /// </summary>
    public static float? LoopbackLevel(string deviceId)
    {
        var m = Master(deviceId);
        if (m is null) return null;
        if (m.QueryHardwareSupport(out var mask) == 0 && (mask & HardwareVolume) != 0) return 1f;
        return m.GetMasterVolumeLevel(out var db) == 0 ? (float)Math.Pow(10, db / 20.0) : null;
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
