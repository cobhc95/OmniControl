namespace OmniControl.Core.Engine;

/// Live peak levels of a meter (a device's, see <see cref="DeviceMeters"/>, or a program's, see <see cref="AudioSession.Peaks"/>).
public static partial class CoreAudio
{
    /// Left and right peak (0 to 1) of one meter; a mono source reports the same value on both sides. Nothing is allocated per read.
    internal static unsafe bool ReadPeaks(IAudioMeterInformation meter, out float left, out float right)
    {
        left = right = 0;
        try
        {
            if (meter.GetMeteringChannelCount(out var n) == 0 && n is >= 2 and <= MaxMeterChannels)
            {
                var v = stackalloc float[(int)n];
                if (meter.GetChannelsPeakValues(n, v) != 0) return false;
                left = v[0]; right = v[1];
                return true;
            }
            if (meter.GetPeakValue(out var p) != 0) return false;
            left = right = p;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return false; /* device vanished: caller reopens the meter */ }
    }

    // More channels than any device has (7.1.4 is 12): a stack buffer that is always big enough.
    private const uint MaxMeterChannels = 64;

    internal static IAudioMeterInformation? OpenMeter(string? id, bool recording)
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
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; /* device has no meter */ }
    }
}

/// The device meters one owner reads (the mixer): each device's meter object is kept between reads and released when it is replaced or
/// the owner is disposed. The default device's meter is opened again every <see cref="CoreTiming.DefaultMeterRefreshMs"/>, so a changed
/// default is followed. Thread-safe.
public sealed class DeviceMeters : IDisposable
{
    private readonly Dictionary<string, (CoreAudio.IAudioMeterInformation? Meter, long At)> _meters = new();
    private readonly object _gate = new();

    /// Peak levels of a device. <paramref name="id"/> null means the default device of that kind.
    public bool Peaks(string? id, bool recording, out float left, out float right)
    {
        left = right = 0;
        var key = (recording ? "c:" : "r:") + (id ?? string.Empty);
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (!_meters.TryGetValue(key, out var hit) || ((id is null || hit.Meter is null) && now - hit.At > CoreTiming.DefaultMeterRefreshMs))
            {
                Release(hit.Meter);
                hit = (CoreAudio.OpenMeter(id, recording), now);
                _meters[key] = hit;
            }
            if (hit.Meter is not null && CoreAudio.ReadPeaks(hit.Meter, out left, out right)) return true;
            Release(hit.Meter);
            _meters.Remove(key); // the device went away: look it up again next time
            return false;
        }
    }

    private static void Release(object? meter)
    {
        if (meter is not null && Marshal.IsComObject(meter)) Marshal.ReleaseComObject(meter);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (meter, _) in _meters.Values) Release(meter);
            _meters.Clear();
        }
    }
}
