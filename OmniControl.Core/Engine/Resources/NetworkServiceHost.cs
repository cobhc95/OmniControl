using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using static OmniControl.Core.Engine.Resources.NetworkServiceSetup;

namespace OmniControl.Core.Engine.Resources;

/// The network service's process: OmniControl.exe started by Windows as LocalSystem. It takes a crashed run's rules out of Windows, then
/// serves one client at a time on a pipe only the user it was installed for (and SYSTEM) may open, answering only <see cref="NetworkPipe"/>'s
/// checked requests through <see cref="NetworkWork"/>. Fail-open: when the client leaves, or the service stops, every rule leaves Windows
/// and download shaping ends; with no client for the idle grace it stops itself; the traffic session ends when no read came for a while.
public sealed class NetworkServiceHost
{
    private readonly SecurityIdentifier _user;
    private readonly NetworkWork _work;
    private readonly TimeSpan _idle, _speedsIdle;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, NetRule> _rules = new(StringComparer.OrdinalIgnoreCase); // by path, what the client asked for
    private readonly object _gate = new(); // the rules and the traffic session
    private readonly object _statusGate = new();
    private readonly Timer _speedsIdleTimer;
    private readonly ServiceMainProc _main; // kept in fields: Windows calls them for as long as the service runs
    private readonly HandlerProc _handler;
    private ITrafficSession? _traffic;
    private volatile string? _downloadFailure;
    private IntPtr _statusHandle;
    private ServiceStatus _status;
    private bool _failed;

    private NetworkServiceHost(SecurityIdentifier user, ShapingTimes shaping, TimeSpan idle, TimeSpan speedsIdle)
    {
        (_user, _idle, _speedsIdle) = (user, idle, speedsIdle);
        _work = new NetworkWork(shaping, reason => _downloadFailure = reason);
        _speedsIdleTimer = new Timer(_ =>
        {
            lock (_gate) StopTraffic();
        });
        (_main, _handler) = (Main, Control);
    }

    /// Runs the service for the account <paramref name="userSid"/> until Windows stops it or it was idle; the process's exit code. Refuses
    /// (1) when not started by Windows' service manager or given anything but one account's SID.
    public static int Run(string userSid, ShapingTimes shaping, TimeSpan idle, TimeSpan speedsIdle)
    {
        SecurityIdentifier user;
        try { user = new SecurityIdentifier(userSid); }
        catch (ArgumentException) { return 1; }
        return user.IsAccountSid() ? new NetworkServiceHost(user, shaping, idle, speedsIdle).Dispatch() : 1;
    }

    private int Dispatch()
    {
        var name = Marshal.StringToHGlobalUni(Name);
        try
        {
            var table = new[] { new TableEntry { Name = name, Main = Marshal.GetFunctionPointerForDelegate(_main) }, default };
            return StartServiceCtrlDispatcherW(table) && !_failed ? 0 : 1;
        }
        finally { Marshal.FreeHGlobal(name); }
    }

    private void Main(int count, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerExW(Name, _handler, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
        {
            _failed = true;
            return;
        }
        Report(StartPending);
        try
        {
            using var pipe = OpenPipe();
            _work.RemoveAll(); // what a crashed run left
            Report(Running);
            Serve(pipe).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // the service's entry point: whatever failed, Windows is put back below
        {
            _failed = true;
        }
        finally
        {
            Report(StopPending);
            lock (_gate)
            {
                StopTraffic();
                _work.StopDownloads();
                _work.RemoveAll();
            }
            _speedsIdleTimer.Dispose();
            Report(Stopped);
        }
    }

    /// The one pipe instance: the user it serves may read and write, SYSTEM all, nobody over the network. First instance only, so a pipe
    /// of the same name made earlier by another program (whose rights would apply) makes the start fail instead.
    private NamedPipeServerStream OpenPipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(_user, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(NetworkPipe.Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        var stop = _stop.Token;
        while (!stop.IsCancellationRequested)
        {
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                idle.CancelAfter(_idle);
                try { await pipe.WaitForConnectionAsync(idle.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; } // idle for the grace, or Windows stops the service
            }
            try { await Talk(pipe, stop).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
            {
                // The client left, broke the protocol, or the service stops: it is let go below like one that closed.
            }
            finally
            {
                lock (_gate) Clear();
                pipe.Disconnect();
            }
        }
    }

    private async Task Talk(NamedPipeServerStream pipe, CancellationToken stop)
    {
        while (await NetworkPipe.ReadAsync(pipe, NetworkPipe.MaxRequest, stop).ConfigureAwait(false) is { } body)
        {
            NetReply reply;
            lock (_gate) reply = Answer(body);
            var message = NetworkPipe.Encode(reply);
            if (message.Length > NetworkPipe.MaxReply) message = NetworkPipe.Encode(Reply("the answer was too large"));
            await NetworkPipe.WriteAsync(pipe, message, NetworkPipe.MaxReply, stop).ConfigureAwait(false);
        }
    }

    private NetReply Answer(byte[] body)
    {
        var (request, refused) = NetworkPipe.Parse(body, File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return request?.Op switch
        {
            null => Reply(refused),
            NetOp.Status => Reply(),
            NetOp.ListRules => Reply() with { Rules = _rules.Values.ToList() },
            NetOp.ReadSpeeds => ReadSpeeds(),
            NetOp.RefreshAdapters => RefreshAdapters(),
            _ => Change(request),
        };
    }

    private NetReply Change(NetRequest request)
    {
        var path = request.Path!;
        var clears = request.Op is NetOp.ClearUpload or NetOp.ClearDownload or NetOp.Unblock;
        if (!_rules.TryGetValue(path, out var held))
        {
            if (clears) return Reply(); // nothing held for it: nothing to take out
            if (_rules.Count >= NetworkPipe.MaxRules) return Reply($"the service holds at most {NetworkPipe.MaxRules} programs");
            held = new NetRule(path);
        }
        var changed = request.Op switch
        {
            NetOp.SetUpload => held with { UploadKbps = request.Kbps },
            NetOp.ClearUpload => held with { UploadKbps = 0 },
            NetOp.SetDownload => held with { DownloadKbps = request.Kbps },
            NetOp.ClearDownload => held with { DownloadKbps = 0 },
            NetOp.Block => held with { Blocked = true },
            _ => held with { Blocked = false },
        };
        if (changed.IsEmpty) _rules.Remove(path);
        else _rules[path] = changed;
        return Reply() with { Problems = Apply() };
    }

    private IReadOnlyList<string> Apply()
    {
        _downloadFailure = null; // a shaper that ended is started again by this sync
        return _work.Sync(_rules.Values.Select(r => r.ToRule()).ToList()).Take(NetworkPipe.MaxProblems).ToList();
    }

    /// The traffic since the last read; the first read starts the session (and hands over nothing yet).
    private NetReply ReadSpeeds()
    {
        _speedsIdleTimer.Change(_speedsIdle, Timeout.InfiniteTimeSpan);
        if (_traffic is null)
        {
            try { _traffic = _work.StartTraffic(); }
            catch (IOException ex) { return Reply() with { SpeedsStopped = ex.Message }; }
            return Reply() with { Speeds = Array.Empty<AppTraffic>() };
        }
        if (_traffic.Stopped is { } why)
        {
            StopTraffic();
            return Reply() with { SpeedsStopped = why };
        }
        return Reply() with
        {
            Speeds = _traffic.Take().Take(NetworkPipe.MaxSpeeds).Select(a => a.Path.Length > NetworkPipe.MaxPath ? a with { Path = string.Empty } : a).ToList(),
        };
    }

    private NetReply RefreshAdapters()
    {
        _work.RefreshAdapters();
        return Reply();
    }

    private void StopTraffic()
    {
        _traffic?.Dispose();
        _traffic = null;
    }

    /// The client left: nothing it asked for stays.
    private void Clear()
    {
        StopTraffic();
        if (_rules.Count == 0) return;
        _rules.Clear();
        Apply();
    }

    private NetReply Reply(string? error = null) => new(NetworkPipe.Version, error, DownloadFailure: _downloadFailure);

    // ---- Windows' service manager ----

    private const uint OwnProcess = 0x10, Stopped = 1, StartPending = 2, StopPending = 3, Running = 4, AcceptStop = 0x1, AcceptShutdown = 0x4, ServiceSpecificError = 1066;
    private const int ControlStop = 1, ControlInterrogate = 4, ControlShutdown = 5, NotImplemented = 120;

    private int Control(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case ControlStop or ControlShutdown:
                Report(StopPending);
                _stop.Cancel();
                return 0;
            case ControlInterrogate:
                return 0;
            default:
                return NotImplemented;
        }
    }

    private void Report(uint state)
    {
        lock (_statusGate)
        {
            var pending = state is StartPending or StopPending;
            _status.ServiceType = OwnProcess;
            _status.CurrentState = state;
            _status.ControlsAccepted = state == Running ? AcceptStop | AcceptShutdown : 0;
            (_status.Win32ExitCode, _status.ServiceSpecificExitCode) = state == Stopped && _failed ? (ServiceSpecificError, 1u) : (0u, 0u);
            _status.CheckPoint = pending ? _status.CheckPoint + 1 : 0;
            _status.WaitHint = pending ? (uint)CoreTiming.NetworkServiceWaitHintMs : 0;
            SetServiceStatus(_statusHandle, ref _status);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TableEntry
    {
        public IntPtr Name, Main;
    }

    private delegate void ServiceMainProc(int count, IntPtr arguments);

    private delegate int HandlerProc(int control, int eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool StartServiceCtrlDispatcherW(TableEntry[] table);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr RegisterServiceCtrlHandlerExW(string name, HandlerProc handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
}
