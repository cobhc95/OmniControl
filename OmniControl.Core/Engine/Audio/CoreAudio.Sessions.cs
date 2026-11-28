namespace OmniControl.Core.Engine;

/// One program session as a plain fact (no COM object held): its process, its playback device and its state.
public readonly record struct SessionFact(int ProcessId, string DeviceId, int State);

/// Per-program audio sessions.
public static partial class CoreAudio
{
    // ---- sessions ----

    /// Every open audio session on every playback device (the system-sounds session is left out).
    /// Releases all temporary enumerator/device/manager/skipped COM objects immediately; the caller owns and disposes the returned sessions.
    public static IReadOnlyList<AudioSession> Sessions()
    {
        var result = new List<AudioSession>();
        IMMDeviceEnumerator? e = null;
        try
        {
            e = NewEnumerator();
            var iid = typeof(IAudioSessionManager2).GUID;
            foreach (var d in ActiveDevices(e))
            {
                object? manager = null;
                IAudioSessionEnumerator? list = null;
                try
                {
                    if (d.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out manager) != 0 || manager is not IAudioSessionManager2 mgr) continue;
                    if (mgr.GetSessionEnumerator(out list) != 0) continue;
                    list.GetCount(out var count);
                    var deviceId = DeviceId(d);
                    for (var i = 0; i < count; i++)
                    {
                        if (list.GetSession(i, out var ctl) != 0) continue;
                        var kept = false;
                        try
                        {
                            if (ctl is not IAudioSessionControl2 c2 || ctl is not ISimpleAudioVolume vol) continue;
                            if (c2.IsSystemSoundsSession() == 0) continue;
                            c2.GetProcessId(out var pid);
                            c2.GetState(out var state);
                            if (pid == 0 || state == 2) continue;
                            result.Add(new AudioSession((int)pid, deviceId, state, c2, vol, ctl as IChannelAudioVolume, ctl as IAudioMeterInformation));
                            kept = true;
                        }
                        finally
                        {
                            if (!kept) ReleaseCom(ctl);
                        }
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException) { /* skip a device that misbehaves */ }
                finally
                {
                    ReleaseCom(list);
                    ReleaseCom(manager);
                    ReleaseCom(d);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { /* no audio service */ }
        finally { ReleaseCom(e); }
        return result;
    }

    /// Every open program session (as <see cref="Sessions"/> lists them) as plain facts: process, device and state. Every COM object this
    /// read takes is given back before it returns, so a scan that only compares can run often without leaving objects for the finalizer.
    public static IReadOnlyList<SessionFact> SessionFacts()
    {
        var result = new List<SessionFact>();
        IMMDeviceEnumerator? e = null;
        try
        {
            e = NewEnumerator();
            var iid = typeof(IAudioSessionManager2).GUID;
            foreach (var d in ActiveDevices(e))
            {
                object? manager = null;
                IAudioSessionEnumerator? list = null;
                try
                {
                    if (d.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out manager) != 0 || manager is not IAudioSessionManager2 mgr) continue;
                    if (mgr.GetSessionEnumerator(out list) != 0) continue;
                    list.GetCount(out var count);
                    var deviceId = DeviceId(d);
                    for (var i = 0; i < count; i++)
                    {
                        if (list.GetSession(i, out var ctl) != 0) continue;
                        try
                        {
                            if (ctl is not IAudioSessionControl2 c2 || c2.IsSystemSoundsSession() == 0) continue;
                            c2.GetProcessId(out var pid);
                            c2.GetState(out var state);
                            if (pid != 0 && state != 2) result.Add(new SessionFact((int)pid, deviceId, state));
                        }
                        finally { ReleaseCom(ctl); }
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException) { /* skip a device that misbehaves */ }
                finally
                {
                    ReleaseCom(list);
                    ReleaseCom(manager);
                    ReleaseCom(d);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { /* no audio service */ }
        finally { ReleaseCom(e); }
        return result;
    }

    /// Full path of a process's executable, or null when it can't be read.
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
