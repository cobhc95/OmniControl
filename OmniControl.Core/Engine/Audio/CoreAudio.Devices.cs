using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>Playback devices.</summary>
public static partial class CoreAudio
{
    // ---- devices ----

    private static string DeviceId(IMMDevice d) => d.GetId(out var id) == 0 ? id : string.Empty;

    private static string DeviceName(IMMDevice d)
    {
        try
        {
            if (d.OpenPropertyStore(0, out var store) != 0) return "Unknown device";
            var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            if (store.GetValue(ref key, out var pv) != 0) return "Unknown device";
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) ?? "Unknown device" : "Unknown device"; }
            finally { PropVariantClear(ref pv); }
        }
        catch { return "Unknown device"; }
    }

    private static List<IMMDevice> ActiveDevices(IMMDeviceEnumerator e, int flow = Render)
    {
        var list = new List<IMMDevice>();
        if (e.EnumAudioEndpoints(flow, StateActive, out var coll) != 0) return list;
        coll.GetCount(out var n);
        for (uint i = 0; i < n; i++)
            if (coll.Item(i, out var d) == 0) list.Add(d);
        return list;
    }

    /// <summary>Playback devices that are plugged in and enabled right now.</summary>
    public static IReadOnlyList<AudioDevice> Devices()
    {
        var result = new List<AudioDevice>();
        try
        {
            var e = NewEnumerator();
            string defaultId = e.GetDefaultAudioEndpoint(Render, Multimedia, out var def) == 0 ? DeviceId(def) : string.Empty;
            foreach (var d in ActiveDevices(e))
            {
                var id = DeviceId(d);
                result.Add(new AudioDevice(id, DeviceName(d), id == defaultId));
            }
        }
        catch { /* no audio service */ }
        return result;
    }

}
