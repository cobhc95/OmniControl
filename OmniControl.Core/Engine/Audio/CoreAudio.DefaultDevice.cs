namespace OmniControl.Core.Engine;

/// Choosing Windows' default playback device (what the Volume page's master plays on).
public static partial class CoreAudio
{
    /// Makes <paramref name="deviceId"/> Windows' default playback device, as Sound settings' "Set as default" does (the console and
    /// multimedia roles; the communications default is left alone). Returns false with a reason when Windows refuses.
    public static bool TrySetDefaultDevice(string deviceId, out string error)
    {
        error = string.Empty;
        try
        {
            var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidPolicyConfig)!)!;
            try
            {
                foreach (var role in new[] { Console, Multimedia })
                {
                    var hr = policy.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                    {
                        error = $"Windows refused the change (0x{hr:X8}).";
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            error = ex.Message; // the interface is undocumented: a Windows build without it says no instead of crashing
            return false;
        }
    }
}
