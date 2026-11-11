using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>Per-program audio sessions.</summary>
public static partial class CoreAudio
{
    // ---- sessions ----

    /// <summary>Every open audio session on every playback device (the system-sounds session is left out).</summary>
    public static IReadOnlyList<AudioSession> Sessions()
    {
        var result = new List<AudioSession>();
        try
        {
            var e = NewEnumerator();
            var iid = typeof(IAudioSessionManager2).GUID;
            foreach (var d in ActiveDevices(e))
            {
                try
                {
                    if (d.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) != 0 || o is not IAudioSessionManager2 mgr) continue;
                    if (mgr.GetSessionEnumerator(out var list) != 0) continue;
                    list.GetCount(out var count);
                    var deviceId = DeviceId(d);
                    for (var i = 0; i < count; i++)
                    {
                        if (list.GetSession(i, out var ctl) != 0) continue;
                        if (ctl is not IAudioSessionControl2 c2 || ctl is not ISimpleAudioVolume vol) continue;
                        if (c2.IsSystemSoundsSession() == 0) continue;
                        c2.GetProcessId(out var pid);
                        c2.GetState(out var state);
                        if (pid == 0 || state == 2) continue;
                        result.Add(new AudioSession((int)pid, deviceId, state, vol, ctl as IChannelAudioVolume, ctl as IAudioMeterInformation));
                    }
                }
                catch { /* skip a device that misbehaves */ }
            }
        }
        catch { /* no audio service */ }
        return result;
    }

    /// <summary>Full path of a process's executable, or null when it can't be read.</summary>
    public static string? ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

}
