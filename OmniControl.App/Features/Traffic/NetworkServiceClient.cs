using System.IO.Pipes;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Traffic;

/// OmniControl's side of the network service (<see cref="NetworkServiceHost"/>): rules and download limits go as the service's fixed requests
/// (only the differences from what it holds) and are read back with ListRules; per-program traffic is read with ReadSpeeds. The connection is
/// held only while a rule or the traffic session needs it (the service then clears everything and stops itself when idle); Windows is asked
/// to start the service when it is not running. Workers only; one request at a time.
internal sealed class NetworkServiceClient : INetworkWork
{
    private readonly Action<string> _downloadFailed;
    private readonly Action _changed, _lost;
    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private Dictionary<string, NetRule> _held = new(StringComparer.OrdinalIgnoreCase); // by program and place (NetRule.Key), as the service last said
    private Traffic? _traffic;
    private volatile bool _connected;
    private volatile string? _problem;

    /// <param name="downloadFailed">Why the service's download shaping ended, whenever an answer says so.</param>
    /// <param name="changed">Connected or <see cref="Problem"/> changed.</param>
    /// <param name="lost">The connection broke while rules were held (outside a sync): they need sending again.</param>
    public NetworkServiceClient(Action<string> downloadFailed, Action changed, Action lost) => (_downloadFailed, _changed, _lost) = (downloadFailed, changed, lost);

    /// True while connected to the running service.
    public bool Connected => _connected;

    /// Why the service could not be reached or answered (one sentence, no full stop), until it answers again.
    public string? Problem => _problem;

    public IReadOnlyList<string> Sync(IReadOnlyList<NetworkRule> wanted)
    {
        var problems = new List<string>();
        var want = new Dictionary<string, NetRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in wanted)
            if (rule.Path.Length > 0 && NetRule.From(rule) is var asked) want[asked.Key] = asked;
            else problems.Add($"Couldn't set the rule of “{rule.AppId}”: Windows did not say where the program is.");
        lock (_gate)
        {
            var tried = new List<string>();
            if (want.Count > 0 || _held.Count > 0)
            {
                var wasConnected = _pipe is not null;
                if (Connect(tried) && !Send(want, tried) && wasConnected)
                {
                    tried.Clear(); // the connection broke since the last sync (the service was restarted): once more, everything sent anew
                    if (Connect(tried)) Send(want, tried);
                }
            }
            problems.AddRange(tried);
            CloseWhenIdle();
        }
        return problems.Distinct().ToList();
    }

    /// Asks for each difference, then reads back what the service holds; false when the connection broke on the way.
    private bool Send(Dictionary<string, NetRule> want, List<string> problems)
    {
        foreach (var key in want.Keys.Union(_held.Keys, StringComparer.OrdinalIgnoreCase).ToList())
            foreach (var request in NetworkPipe.Changes((want.GetValueOrDefault(key) ?? _held[key]).Path, _held.GetValueOrDefault(key), want.GetValueOrDefault(key)))
                if (Ask(request, problems) is null) return false;
        if (!ReadRules(problems)) return false;
        if (want.Count != _held.Count || want.Any(w => !_held.TryGetValue(w.Key, out var held) || !held.SameLimits(w.Value)))
            problems.Add("The network service does not hold the rules as set.");
        return true;
    }

    private bool ReadRules(List<string> problems)
    {
        if (Ask(Request(NetOp.ListRules), problems) is not { Rules: { } rules }) return false;
        _held = rules.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    public void RefreshAdapters()
    {
        lock (_gate)
            if (_pipe is not null && _held.Values.Any(r => r.DownloadKbps > 0 || r.DownloadBlocked)) Ask(Request(NetOp.RefreshAdapters), new List<string>());
    }

    /// Nothing to send: the service ends its download limits with the rest when the connection closes (<see cref="RemoveAll"/>).
    public void StopDownloads() { }

    /// Closes the connection; the service takes every rule out of Windows when it sees it go.
    public IReadOnlyList<string> RemoveAll()
    {
        lock (_gate) Drop(null);
        return Array.Empty<string>();
    }

    public ITrafficSession StartTraffic()
    {
        lock (_gate)
        {
            if (!Connect(new List<string>())) throw new IOException(_problem);
            var traffic = _traffic = new Traffic(this);
            ReadSpeeds(traffic); // the service's session starts with the first read
            if (traffic.Stopped is not { } why) return traffic;
            EndTraffic(traffic);
            throw new IOException(why);
        }
    }

    private IReadOnlyList<AppTraffic> ReadSpeeds(Traffic traffic)
    {
        lock (_gate)
        {
            if (traffic != _traffic) return Array.Empty<AppTraffic>();
            var hadRules = _held.Count > 0;
            if (Ask(Request(NetOp.ReadSpeeds), new List<string>()) is not { } reply)
            {
                if (hadRules) _lost();
                return Array.Empty<AppTraffic>();
            }
            traffic.Stopped ??= reply.SpeedsStopped;
            return reply.Speeds ?? Array.Empty<AppTraffic>();
        }
    }

    private void EndTraffic(Traffic traffic)
    {
        lock (_gate)
        {
            if (traffic != _traffic) return;
            _traffic = null;
            CloseWhenIdle();
        }
    }

    // ---- The connection ----

    private static NetRequest Request(NetOp op) => new(NetworkPipe.Version, op);

    /// Connects (asking Windows to start the service first) and reads what it holds; false with the reason in the problems when it cannot.
    private bool Connect(List<string> problems)
    {
        if (_pipe is not null) return true;
        if (NetworkServiceSetup.Start() is { } failed) return Fail(failed, problems);
        var pipe = new NamedPipeClientStream(".", NetworkPipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { pipe.Connect(Timing.NetworkServiceConnectWait); }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            return Fail(ex is UnauthorizedAccessException ? "The network service serves another Windows user" : "The network service did not answer", problems);
        }
        if (NetworkServiceSetup.ServerProblem(pipe.SafePipeHandle) is { } impostor)
        {
            pipe.Dispose(); // nothing was sent to it
            return Fail(impostor, problems);
        }
        _pipe = pipe;
        if (!ReadRules(problems)) return false;
        State(true, null);
        return true;
    }

    private bool Fail(string why, List<string> problems)
    {
        State(false, why);
        problems.Add($"Network limits not applied: {why}.");
        return false;
    }

    /// One request and its answer; null when the connection broke (it is dropped and the reason is in the problems).
    private NetReply? Ask(NetRequest request, List<string> problems)
    {
        NetReply reply;
        try { reply = Exchange(request); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            var why = ex is InvalidDataException ? ex.Message : "The network service stopped answering";
            Drop(why);
            problems.Add($"Network limits not applied: {why}.");
            return null;
        }
        if (reply.Error is { } refused) problems.Add($"The network service refused {request.Op}{(request.Path is { } path ? $" for {Path.GetFileName(path)}" : string.Empty)}: {refused}.");
        if (reply.Problems is { } more) problems.AddRange(more);
        if (reply.DownloadFailure is { } failure) _downloadFailed(failure);
        return reply;
    }

    private NetReply Exchange(NetRequest request)
    {
        using var timeout = new CancellationTokenSource(Timing.NetworkServiceReplyWait);
        NetworkPipe.WriteAsync(_pipe!, NetworkPipe.Encode(request), NetworkPipe.MaxRequest, timeout.Token).GetAwaiter().GetResult();
        var body = NetworkPipe.ReadAsync(_pipe!, NetworkPipe.MaxReply, timeout.Token).GetAwaiter().GetResult()
            ?? throw new EndOfStreamException();
        var reply = NetworkPipe.ReadReply(body) ?? throw new InvalidDataException("The network service's answer could not be read");
        return reply.V == NetworkPipe.Version ? reply : throw new InvalidDataException("The network service is from another OmniControl version; install it again");
    }

    private void CloseWhenIdle()
    {
        if (_held.Count == 0 && _traffic is null) Drop(null);
    }

    /// Lets the connection go (<paramref name="why"/>: it broke); a traffic session reading through it stops with the reason.
    private void Drop(string? why)
    {
        _pipe?.Dispose();
        _pipe = null;
        _held.Clear();
        if (_traffic is not null) _traffic.Stopped ??= why ?? "The network service closed";
        State(false, why);
    }

    private void State(bool connected, string? problem)
    {
        if (connected == _connected && problem == _problem) return;
        (_connected, _problem) = (connected, problem);
        _changed();
    }

    private sealed class Traffic(NetworkServiceClient client) : ITrafficSession
    {
        public string? Stopped { get; set; }

        public IReadOnlyList<AppTraffic> Take() => Stopped is null ? client.ReadSpeeds(this) : Array.Empty<AppTraffic>();

        public void Dispose() => client.EndTraffic(this);
    }
}
