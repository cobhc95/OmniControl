using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine.Resources;

/// The network service in Windows' service list (LocalSystem, started on demand): <see cref="Install"/> and <see cref="Remove"/> need
/// administrator rights (OmniControl's elevated helper start); <see cref="Installed"/> and <see cref="Start"/> work for the user it was
/// installed for, who may start and stop it and nothing more. Call off the UI thread.
public static class NetworkServiceSetup
{
    public const string Name = "OmniControlNetwork"; // retyped in tools/setup.iss
    private const string DisplayName = "OmniControl Network service";
    private const string Description = "Sets OmniControl's per-app network limits and reads per-app speeds, so OmniControl needs no administrator rights for them. Started by OmniControl when needed; stops itself when idle.";

    /// True when the service is in Windows' list (also when it was installed for another user).
    public static bool Installed
    {
        get
        {
            using var manager = OpenSCManagerW(null, null, ScConnect);
            if (manager.IsInvalid) return false;
            using var service = OpenServiceW(manager, Name, QueryStatus);
            return !service.IsInvalid || Marshal.GetLastWin32Error() == AccessDenied;
        }
    }

    /// Adds the service, or points it at <paramref name="commandLine"/> again, as demand-start LocalSystem; only <paramref name="user"/> (with
    /// SYSTEM and administrators) may start and stop it. Null when done, else one plain sentence.
    public static string? Install(string commandLine, SecurityIdentifier user)
    {
        using var manager = OpenSCManagerW(null, null, ScConnect | ScCreate);
        if (manager.IsInvalid) return Failed("open Windows' service list");
        var service = OpenServiceW(manager, Name, AllAccess);
        try
        {
            if (service.IsInvalid)
            {
                service.Dispose();
                service = CreateServiceW(manager, Name, DisplayName, AllAccess, OwnProcess, DemandStart, ErrorNormal, commandLine, null, IntPtr.Zero, null, null, null);
                if (service.IsInvalid) return Failed("add the network service");
            }
            else if (!ChangeServiceConfigW(service, NoChange, DemandStart, NoChange, commandLine, null, IntPtr.Zero, null, null, null, DisplayName))
                return Failed("update the network service");
            var description = new ServiceDescription { Text = Description };
            if (!ChangeServiceConfig2W(service, ConfigDescription, ref description)) return Failed("describe the network service");
            // Windows' default rights for SYSTEM, administrators and interactive users, plus start and stop for the one user it serves.
            var rights = new RawSecurityDescriptor($"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWRPWPLORC;;;{user.Value})");
            var binary = new byte[rights.BinaryLength];
            rights.GetBinaryForm(binary, 0);
            return SetServiceObjectSecurity(service, DaclInformation, binary) ? null : Failed("set who may start the network service");
        }
        finally { service.Dispose(); }
    }

    /// Stops the service (its rules leave Windows as it stops), waits up to <paramref name="stopWait"/>, and deletes it. Null when done or
    /// there was none, else one plain sentence.
    public static string? Remove(TimeSpan stopWait)
    {
        using var manager = OpenSCManagerW(null, null, ScConnect);
        if (manager.IsInvalid) return Failed("open Windows' service list");
        using var service = OpenServiceW(manager, Name, Stop | QueryStatus | Delete);
        if (service.IsInvalid) return Marshal.GetLastWin32Error() == DoesNotExist ? null : Failed("open the network service");
        var status = new ServiceStatus();
        if (ControlService(service, ControlStop, ref status) || Marshal.GetLastWin32Error() == NotActive)
        {
            var until = Environment.TickCount64 + (long)stopWait.TotalMilliseconds;
            while (QueryServiceStatus(service, ref status) && status.CurrentState != Stopped && Environment.TickCount64 < until) Thread.Sleep(CoreTiming.NetworkServicePollMs);
        }
        return DeleteService(service) || Marshal.GetLastWin32Error() == MarkedForDelete ? null : Failed("remove the network service");
    }

    /// Asks Windows to start the service (as the user it serves). Null when it runs or starts, else one plain sentence (no full stop).
    public static string? Start()
    {
        using var manager = OpenSCManagerW(null, null, ScConnect);
        if (manager.IsInvalid) return Failed("open Windows' service list");
        using var service = OpenServiceW(manager, Name, StartAccess);
        if (service.IsInvalid) return Marshal.GetLastWin32Error() switch
        {
            DoesNotExist => "The network service is not installed",
            AccessDenied => "The network service was installed for another Windows user",
            var error => Says("open the network service", error),
        };
        return StartServiceW(service, 0, IntPtr.Zero) || Marshal.GetLastWin32Error() == AlreadyRunning ? null : Failed("start the network service");
    }

    private static string Failed(string what) => Says(what, Marshal.GetLastWin32Error());

    private static string Says(string what, int error) => $"Windows couldn't {what}: {new Win32Exception(error).Message.TrimEnd('.')}";

    // ---- Service Control Manager ----

    private const uint ScConnect = 0x1, ScCreate = 0x2;
    private const uint QueryStatus = 0x4, StartAccess = 0x10, Stop = 0x20, Delete = 0x10000, AllAccess = 0xF01FF, DaclInformation = 4;
    private const uint OwnProcess = 0x10, DemandStart = 3, ErrorNormal = 1, NoChange = 0xFFFFFFFF, ConfigDescription = 1, ControlStop = 1, Stopped = 1;
    private const int AccessDenied = 5, AlreadyRunning = 1056, DoesNotExist = 1060, NotActive = 1062, MarkedForDelete = 1072;

    internal sealed class ScHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScHandle() : base(true) { }

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        public string Text;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ScHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ScHandle OpenServiceW(ScHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ScHandle CreateServiceW(ScHandle manager, string name, string displayName, uint access, uint type, uint start, uint error,
        string path, string? group, IntPtr tag, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(ScHandle service, uint type, uint start, uint error, string path, string? group, IntPtr tag,
        string? dependencies, string? account, string? password, string displayName);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool ChangeServiceConfig2W(ScHandle service, uint level, ref ServiceDescription info);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetServiceObjectSecurity(ScHandle service, uint information, byte[] descriptor);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(ScHandle service, uint control, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(ScHandle service, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(ScHandle service);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool StartServiceW(ScHandle service, uint count, IntPtr arguments);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CloseServiceHandle(IntPtr handle);
}
