using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>Live peak levels of the default playback device, a recording device, or a program (see <see cref="AudioSession.Peaks"/>).</summary>
public static partial class CoreAudio
{
    private static readonly Dictionary<string, (IAudioMeterInformation? Meter, long At)> _meters = new();
    private const long DefaultRefreshMs = 1500;

    /// <summary>Left and right peak (0 to 1) of one meter; a mono source reports the same value on both sides.</summary>
    internal static bool ReadPeaks(IAudioMeterInformation meter, out float left, out float right)
    {
        left = right = 0;
        try
        {
            if (meter.GetMeteringChannelCount(out var n) == 0 && n >= 2)
            {
                var v = new float[n];
                if (meter.GetChannelsPeakValues(n, v) != 0) return false;
                left = v[0]; right = v[1];
                return true;
            }
            if (meter.GetPeakValue(out var p) != 0) return false;
            left = right = p;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Peak levels of a device. <paramref name="id"/> null means the default device of that kind. The meter object is kept between calls
    /// (the default device is looked up again every second and a half, so a changed default is followed).
    /// </summary>
    public static bool DevicePeaks(string? id, bool recording, out float left, out float right)
    {
        left = right = 0;
        var key = (recording ? "c:" : "r:") + (id ?? string.Empty);
        var now = Environment.TickCount64;
        if (!_meters.TryGetValue(key, out var hit) || (id is null && now - hit.At > DefaultRefreshMs) || (hit.Meter is null && now - hit.At > DefaultRefreshMs))
        {
            hit = (OpenMeter(id, recording), now);
            _meters[key] = hit;
        }
        if (hit.Meter is not null && ReadPeaks(hit.Meter, out left, out right)) return true;
        _meters.Remove(key); // the device went away: look it up again next time
        return false;
    }

    private static IAudioMeterInformation? OpenMeter(string? id, bool recording)
    {
        try
        {
            var e = NewEnumerator();
            IMMDevice? dev = null;
            if (id is null) { if (e.GetDefaultAudioEndpoint(recording ? Capture : Render, Multimedia, out var d) == 0) dev = d; }
            else if (e.GetDevice(id, out var d2) == 0) dev = d2;
            if (dev is null) return null;
            var iid = typeof(IAudioMeterInformation).GUID;
            return dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) == 0 ? o as IAudioMeterInformation : null;
        }
        catch { return null; }
    }
}
