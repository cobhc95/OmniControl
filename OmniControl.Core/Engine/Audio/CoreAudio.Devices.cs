namespace OmniControl.Core.Engine;

/// Playback devices.
public static partial class CoreAudio
{
    // ---- devices ----

    private static string DeviceId(IMMDevice d) => d.GetId(out var id) == 0 ? id : string.Empty;

    private static string DeviceName(IMMDevice d)
    {
        IPropertyStore? store = null;
        try
        {
            if (d.OpenPropertyStore(0, out store) != 0) return "Unknown device";
            var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            if (store.GetValue(ref key, out var pv) != 0) return "Unknown device";
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) ?? "Unknown device" : "Unknown device"; }
            finally { PropVariantClear(ref pv); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return "Unknown device"; }
        finally { ReleaseCom(store); }
    }

    private static List<IMMDevice> ActiveDevices(IMMDeviceEnumerator e, int flow = Render)
    {
        var list = new List<IMMDevice>();
        if (e.EnumAudioEndpoints(flow, StateActive, out var coll) != 0) return list;
        try
        {
            coll.GetCount(out var n);
            for (uint i = 0; i < n; i++)
                if (coll.Item(i, out var d) == 0) list.Add(d);
        }
        finally { ReleaseCom(coll); }
        return list;
    }

    /// Playback devices that are plugged in and enabled right now.
    public static IReadOnlyList<AudioDevice> Devices() => DeviceList(Render);

    /// The playback (<see cref="Render"/>) or recording (<see cref="Capture"/>) devices plugged in and enabled now, as plain facts; every
    /// COM object this read takes is given back before it returns (the mixer reads them every few seconds).
    private static IReadOnlyList<AudioDevice> DeviceList(int flow)
    {
        var result = new List<AudioDevice>();
        IMMDeviceEnumerator? e = null;
        try
        {
            e = NewEnumerator();
            var defaultId = string.Empty;
            if (e.GetDefaultAudioEndpoint(flow, Multimedia, out var def) == 0)
            {
                defaultId = DeviceId(def);
                ReleaseCom(def);
            }
            foreach (var d in ActiveDevices(e, flow))
            {
                try
                {
                    var id = DeviceId(d);
                    result.Add(new AudioDevice(id, DeviceName(d), id == defaultId));
                }
                finally { ReleaseCom(d); }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { /* no audio service */ }
        finally { ReleaseCom(e); }
        return result;
    }

}
