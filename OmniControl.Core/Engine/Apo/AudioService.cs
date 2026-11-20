namespace OmniControl.Core.Engine.Apo;

/// Restarts Windows audio (AudioSrv and the services that depend on it) so a device's new effects load: a port of Equalizer APO's
/// ServiceHelper::restartService (Equalizer APO 1.4.2, helpers/ServiceHelper.cpp, GPL-2.0-or-later, Copyright (C) 2024 Jonas Thedering).
/// Needs administrator rights; the elevated helper calls it once per change.
internal static class AudioService
{
    private const string Name = "AudioSrv";
    private const uint ManagerAllAccess = 0xF003F, ServiceAccess = 0x0010 | 0x0020 | 0x0004 | 0x0008; // start, stop, query, enumerate dependents
    private const uint ControlStop = 1, Stopped = 1, Running = 4, ActiveServices = 1, StatusProcessInfo = 0, MoreData = 234;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool StartService(IntPtr service, int argc, IntPtr argv);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, uint level, out ServiceStatusProcess buffer, int size, out int needed);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool EnumDependentServices(IntPtr service, uint state, IntPtr buffer, int size, out int needed, out int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint Type, State, Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess { public uint Type, State, Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint, ProcessId, Flags; }

    // ENUM_SERVICE_STATUSW: two string pointers, then a SERVICE_STATUS.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EnumStatus { public IntPtr ServiceName, DisplayName; public ServiceStatus Status; }

    /// Stops the dependents first, then AudioSrv, and starts them again in reverse order. Null when it worked, else what failed.
    public static string? Restart()
    {
        var manager = OpenSCManager(null, null, ManagerAllAccess);
        if (manager == IntPtr.Zero) return $"OpenSCManager failed ({new Win32Exception().Message})";
        var opened = new List<(string Name, IntPtr Handle)>();
        try
        {
            var main = OpenService(manager, Name, ServiceAccess);
            if (main == IntPtr.Zero) return $"OpenService failed for {Name} ({new Win32Exception().Message})";
            opened.Add((Name, main));
            if (State(main) == Running)
                foreach (var dependent in Dependents(main))
                {
                    var handle = OpenService(manager, dependent, ServiceAccess);
                    if (handle == IntPtr.Zero) return $"OpenService failed for {dependent} ({new Win32Exception().Message})";
                    opened.Insert(opened.Count - 1, (dependent, handle));
                }

            var clock = Stopwatch.StartNew();
            foreach (var (name, handle) in opened)
            {
                var state = State(handle);
                if (state == Running) state = ControlService(handle, ControlStop, out var status) ? status.State : throw new Win32Exception();
                while (state != Stopped)
                {
                    if (clock.ElapsedMilliseconds > CoreTiming.AudioRestartLimitMs) return $"Service stop timed out on service \"{name}\"";
                    Thread.Sleep(CoreTiming.AudioRestartPollMs);
                    state = State(handle);
                }
            }

            for (var i = opened.Count - 1; i >= 0; i--)
            {
                var (name, handle) = opened[i];
                Start(handle);
                var retryAt = (long)CoreTiming.AudioRestartRetryMs;
                while (State(handle) != Running)
                {
                    var elapsed = clock.ElapsedMilliseconds;
                    if (elapsed > CoreTiming.AudioRestartLimitMs) return $"Service start timed out on service \"{name}\"";
                    if (elapsed > retryAt)
                    {
                        Start(handle); // sometimes the service won't start on the first try
                        retryAt = elapsed + CoreTiming.AudioRestartRetryMs;
                    }
                    Thread.Sleep(CoreTiming.AudioRestartPollMs);
                }
            }
            return null;
        }
        catch (Win32Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            foreach (var (_, handle) in opened) CloseServiceHandle(handle);
            CloseServiceHandle(manager);
        }
    }

    private static uint State(IntPtr service) =>
        QueryServiceStatusEx(service, StatusProcessInfo, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _) ? status.State : throw new Win32Exception();

    /// A start that is refused because it is already starting is fine: the loop waits for it to run.
    private static void Start(IntPtr service)
    {
        const int alreadyRunning = 1056;
        if (!StartService(service, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() is var error and not alreadyRunning) throw new Win32Exception(error);
    }

    private static IReadOnlyList<string> Dependents(IntPtr service)
    {
        if (EnumDependentServices(service, ActiveServices, IntPtr.Zero, 0, out var needed, out _)) return Array.Empty<string>();
        if (Marshal.GetLastWin32Error() != MoreData) throw new Win32Exception();
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumDependentServices(service, ActiveServices, buffer, needed, out _, out var count)) throw new Win32Exception();
            var size = Marshal.SizeOf<EnumStatus>();
            return Enumerable.Range(0, count)
                .Select(i => Marshal.PtrToStringUni(Marshal.PtrToStructure<EnumStatus>(buffer + i * size).ServiceName) ?? string.Empty).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
