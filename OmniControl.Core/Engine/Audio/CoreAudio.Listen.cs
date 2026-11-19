namespace OmniControl.Core.Engine;

/// Windows' own "Listen to this device" of a recording device (Sound control panel, Listen tab): Windows plays the input on an output
/// itself, so OmniControl adds no delay. Read and written through the policy config, as the control panel does.
public static partial class CoreAudio
{
    // The Listen tab's two values on a capture endpoint: pid 1 = listening on (VT_BOOL), pid 0 = the output's endpoint id (VT_LPWSTR, empty = default).
    private static readonly Guid ListenFormat = new("24DBB0FC-9311-4B3D-9CF0-18FF155639D4");
    private const ushort VtBool = 11, VtLpwstr = 31;

    /// Whether Windows plays <paramref name="captureId"/> on an output, and on which (null = the default output). False when it cannot be read.
    public static bool GetListen(string captureId, out bool on, out string? outputId)
    {
        bool isOn = false; string? target = null;
        var ok = WithPolicy(policy =>
        {
            var k = new PropertyKey { fmtid = ListenFormat, pid = 1 };
            if (policy.GetPropertyValue(captureId, false, ref k, out var v) == 0)
            {
                isOn = v.vt == VtBool && (short)(v.p.ToInt64() & 0xFFFF) != 0;
                PropVariantClear(ref v);
            }
            var t = new PropertyKey { fmtid = ListenFormat, pid = 0 };
            if (policy.GetPropertyValue(captureId, false, ref t, out var tv) == 0)
            {
                if (tv.vt == VtLpwstr && tv.p != IntPtr.Zero) target = Marshal.PtrToStringUni(tv.p) is { Length: > 0 } s ? s : null;
                PropVariantClear(ref tv);
            }
            return 0;
        }, out _);
        on = isOn; outputId = target;
        return ok;
    }

    /// Turns listening on or off and sets its output (null = the default output). Returns false with a one-sentence reason when Windows refuses.
    public static bool TrySetListen(string captureId, bool on, string? outputId, out string error) =>
        WithPolicy(policy =>
        {
            var t = new PropertyKey { fmtid = ListenFormat, pid = 0 };
            var tv = new PropVariant { vt = VtLpwstr, p = Marshal.StringToCoTaskMemUni(outputId ?? string.Empty) };
            try
            {
                var hr = policy.SetPropertyValue(captureId, false, ref t, ref tv);
                if (hr != 0) return hr;
            }
            finally { Marshal.FreeCoTaskMem(tv.p); }
            var k = new PropertyKey { fmtid = ListenFormat, pid = 1 };
            var v = new PropVariant { vt = VtBool, p = new IntPtr(on ? 0xFFFF : 0) };
            return policy.SetPropertyValue(captureId, false, ref k, ref v);
        }, out error);

    /// Runs one call on a policy config object, released afterwards; a non-zero result or a missing interface becomes the reason.
    private static bool WithPolicy(Func<IPolicyConfig, int> call, out string error)
    {
        error = string.Empty;
        try
        {
            var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidPolicyConfig)!)!;
            try
            {
                var hr = call(policy);
                if (hr == 0) return true;
                error = $"Windows refused the change (0x{hr:X8}).";
                return false;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            error = ex.Message; // the interface is undocumented: a Windows build without it says no instead of crashing
            return false;
        }
    }
}
