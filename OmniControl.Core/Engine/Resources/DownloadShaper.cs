using System.Buffers;
using System.Net.NetworkInformation;

namespace OmniControl.Core.Engine.Resources;

/// One program's download limit: its identity (<see cref="ProgramIdentity.Normalize"/>) and the most bytes per second it may receive.
public sealed record DownloadLimit(string AppId, long BytesPerSecond);

/// How the shaper paces: the burst a limit lets through at once, the most a program's packets may be held back (more is dropped), how old
/// the port-to-process tables may be while a limited program owns ports (<see cref="Ports"/>) and while none does (<see cref="PortsIdle"/>),
/// the least gap between two reads for a packet to a port not in them (<see cref="Miss"/>), and how long stopping waits for the packet loop.
public sealed record ShapingTimes(TimeSpan Burst, TimeSpan Queue, TimeSpan Ports, TimeSpan PortsIdle, TimeSpan Miss, TimeSpan StopWait);

/// Download limits per program through Windows Packet Filter (<see cref="PacketFilter"/>). One worker reads which process owns each local
/// port (<see cref="PortOwners"/>); the driver is opened only while a limited program owns ports, and meanwhile its filter table hands over
/// every received TCP and UDP packet, so a new connection is limited from its first packet: a port not in the tables makes the worker read
/// them again at once (at most every <see cref="ShapingTimes.Miss"/>; a port still unknown passes). The worker sends each packet straight on
/// unless its program has a limit; then one token bucket per
/// program, whatever adapter the packet came on, lets packets through at the limit, holds them in the program's queue meanwhile and drops
/// one only when the queue is full (TCP then slows down by itself). Fail-open: on any failure the held packets go on, the driver is let go
/// (Windows then delivers every packet itself) and <c>stopped</c> is told why.
public sealed unsafe class DownloadShaper : IDisposable
{
    private const int MinPacket = 28; // the smallest IPv4 packet with a TCP or UDP port in it
    // One failed re-injection can be one odd packet; this many in a row means nothing gets through, so the shaper stops (fail-open).
    private const int SendFailuresToStop = 32;
    private const int Stop = 0, Wake = 1, Arrived = 2;

    private readonly ShapingTimes _times;
    private readonly Action<string> _stopped;
    private readonly PacketHandler _pass;
    private readonly ManualResetEvent _stop = new(false);
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _wakeLock = new();
    private int _stopping, _disposed;
    private readonly Thread _loop;
    // The rest belongs to the loop thread only.
    private readonly Dictionary<string, Bucket> _buckets = new();
    private readonly Dictionary<int, int> _owners = new();
    private readonly ProcessPrograms _programs = new();
    private PacketFilter? _port; // open only while a limited program owns ports
    private WaitHandle[] _waits;
    private long _ownersRead, _ownersFollowed = -1;
    private int _sendFailures, _adaptersChanged;
    private IReadOnlyList<DownloadLimit>? _applied;
    private volatile IReadOnlyList<DownloadLimit> _limits = Array.Empty<DownloadLimit>();

    private DownloadShaper(ShapingTimes times, Action<string> stopped)
    {
        (_times, _stopped) = (times, stopped);
        _pass = Pass;
        _waits = new WaitHandle[] { _stop, _wake };
        _loop = new Thread(Run) { IsBackground = true, Name = "DownloadShaper" };
        _loop.Start();
    }

    /// True when Windows Packet Filter's driver is installed (nothing is opened).
    public static bool Installed => PacketFilter.Installed;

    /// True when a VPN tunnel without Ethernet frames is up (WinTun: WireGuard, Tailscale), whose traffic the filter leaves alone, so a
    /// limit does not reach it. Such tunnels are IF_TYPE_PROP_VIRTUAL (53), a type .NET's enum does not name. Slow: workers only.
    public static bool TunnelUp => NetworkInterface.GetAllNetworkInterfaces()
        .Any(n => (int)n.NetworkInterfaceType == PropVirtual && n.OperationalStatus == OperationalStatus.Up);

    private const int PropVirtual = 53;

    /// Starts shaping (the driver opens once a limited program owns ports). <paramref name="stopped"/> is called on the packet thread with one
    /// plain sentence if shaping ends by itself (the driver could not open or failed).
    public static DownloadShaper Start(ShapingTimes times, Action<string> stopped) => new(times, stopped);

    /// False once the packet loop has ended (stopped, or failed and said why).
    public bool Running => _loop.IsAlive;

    /// The limits from now on (any thread); a program left out is let go and its held packets sent on. An equal list changes nothing.
    public void SetLimits(IReadOnlyList<DownloadLimit> limits)
    {
        if (limits.SequenceEqual(_limits)) return;
        _limits = limits;
        Nudge();
    }

    // A late call (rules removed or adapters changing while shaping stops) must not touch the freed event.
    private void Nudge()
    {
        lock (_wakeLock)
            if (Volatile.Read(ref _disposed) == 0) _wake.Set();
    }

    /// Windows' adapters may have changed (any thread): the packet thread lets an open driver pick up a new one (a VPN connecting).
    public void RefreshAdapters()
    {
        Interlocked.Exchange(ref _adaptersChanged, 1);
        Nudge();
    }

    /// Stops shaping: held packets go on and the driver is let go.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return;
        _stop.Set();
        if (Thread.CurrentThread == _loop || !_loop.Join(_times.StopWait)) return;
        lock (_wakeLock) Interlocked.Exchange(ref _disposed, 1);
        _stop.Dispose();
        _wake.Dispose();
    }

    private void Run()
    {
        string? failure = null;
        try
        {
            while (true)
            {
                var woke = WaitHandle.WaitAny(_waits, Soonest(Release(), Watch()));
                if (woke == Stop) break;
                if (woke == Arrived) _port!.Take(_pass);
                else if (woke == Wake && Interlocked.Exchange(ref _adaptersChanged, 0) == 1) _port?.Refresh();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // the thread's entry point: a failure must never end the process
        {
            failure = ex is Win32Exception win ? PacketFilter.Says(win.NativeErrorCode) : ex.Message;
        }
        finally
        {
            Close();
        }
        if (failure is not null) _stopped(failure);
    }

    /// Sends what is held and lets the driver go (it never throws); Windows then delivers every packet itself.
    private void Close()
    {
        foreach (var bucket in _buckets.Values) Flush(bucket, strict: false);
        _port?.Dispose();
        _port = null;
        _waits = new WaitHandle[] { _stop, _wake };
    }

    /// A packet arrived: straight on, held for its program's limit, or dropped when that program's queue is full.
    private void Pass(byte* record, int length, int ipAt, int ipLength)
    {
        if (ipAt < 0 || BucketOf(record + ipAt, ipLength) is not { } bucket)
        {
            Send(record, length);
            return;
        }
        bucket.Refill(Stopwatch.GetTimestamp());
        if (bucket.Packets.Count == 0 && bucket.Tokens > 0)
        {
            bucket.Tokens -= ipLength;
            Send(record, length);
            return;
        }
        if (bucket.Queued + length > bucket.QueueLimit) return;
        var data = ArrayPool<byte>.Shared.Rent(length);
        new Span<byte>(record, length).CopyTo(data);
        bucket.Packets.Enqueue((data, length, ipLength));
        bucket.Queued += length;
    }

    /// The limited program a packet is for (by its IP version, protocol and destination port), or null.
    private Bucket? BucketOf(byte* p, int length)
    {
        if (_buckets.Count == 0 || length < MinPacket) return null;
        var (header, protocol) = (p[0] >> 4) switch
        {
            4 => ((p[0] & 0xF) * 4, (int)p[9]),
            6 => (40, (int)p[6]), // a packet with IPv6 extension headers (rare) is passed on without a limit
            _ => (0, 0),
        };
        if ((protocol != PortOwners.Tcp && protocol != PortOwners.Udp) || length < header + 4) return null;
        // The driver opens or closes in Watch, never inside a Take; a read here only allocates in Windows' tables and for a new process.
        if (Stopwatch.GetElapsedTime(_ownersRead) > _times.Ports) ReadOwners();
        var key = PortOwners.Key(protocol, p[header + 2] << 8 | p[header + 3]);
        if (!_owners.TryGetValue(key, out var pid))
        {
            if (Stopwatch.GetElapsedTime(_ownersRead) < _times.Miss) return null;
            ReadOwners();
            if (!_owners.TryGetValue(key, out pid)) return null;
        }
        return _buckets.TryGetValue(_programs.Of(pid).Id, out var bucket) ? bucket : null;
    }

    /// Sends every held packet whose time has come; returns the milliseconds until the next one is due, or <see cref="Timeout.Infinite"/>.
    private int Release()
    {
        ApplyLimits();
        var next = Timeout.Infinite;
        var now = Stopwatch.GetTimestamp();
        foreach (var bucket in _buckets.Values)
        {
            if (bucket.Packets.Count == 0) continue;
            bucket.Refill(now);
            while (bucket.Packets.Count > 0 && bucket.Tokens > 0) SendHeld(bucket, strict: true);
            if (bucket.Packets.Count == 0) continue;
            next = Soonest(next, Math.Max(1, (int)Math.Ceiling(-bucket.Tokens * 1000 / bucket.Rate)));
        }
        return next;
    }

    /// Keeps the port tables fresh while a limit is set: read again every <see cref="ShapingTimes.Ports"/> while a limited program owns ports
    /// (a port another program took over), every <see cref="ShapingTimes.PortsIdle"/> while none does (a limited program that starts is
    /// limited from then on). Returns the milliseconds until the next read, or <see cref="Timeout.Infinite"/>.
    private int Watch()
    {
        if (_buckets.Count == 0)
        {
            if (_port is not null) Close();
            return Timeout.Infinite;
        }
        if (Stopwatch.GetElapsedTime(_ownersRead) >= Every) ReadOwners();
        if (_ownersFollowed != _ownersRead) FollowOwners();
        return Math.Max(1, (int)Math.Ceiling((Every - Stopwatch.GetElapsedTime(_ownersRead)).TotalMilliseconds));
    }

    private TimeSpan Every => _port is null ? _times.PortsIdle : _times.Ports;

    private void ReadOwners()
    {
        PortOwners.Fill(_owners);
        _programs.Keep(_owners.Values);
        _ownersRead = Stopwatch.GetTimestamp();
    }

    /// Opens the driver when a limited program owns a port and closes it when none does. Opening throws a <see cref="Win32Exception"/> when
    /// the driver cannot be used.
    private void FollowOwners()
    {
        _ownersFollowed = _ownersRead;
        if (!_owners.Values.Any(pid => _buckets.ContainsKey(_programs.Of(pid).Id)))
        {
            if (_port is not null) Close();
            return;
        }
        if (_port is not null) return;
        _port = PacketFilter.Open();
        _waits = new[] { _stop, _wake, _port.Arrived };
    }

    private static int Soonest(int a, int b) => a == Timeout.Infinite ? b : b == Timeout.Infinite ? a : Math.Min(a, b);

    private void ApplyLimits()
    {
        var limits = _limits;
        if (ReferenceEquals(limits, _applied)) return;
        _applied = limits;
        _ownersRead = 0; // another program may be limited now: its ports go to the driver at once
        foreach (var gone in _buckets.Keys.Where(id => limits.All(l => l.AppId != id)).ToList())
        {
            Flush(_buckets[gone], strict: true);
            _buckets.Remove(gone);
        }
        foreach (var limit in limits)
        {
            var fresh = !_buckets.TryGetValue(limit.AppId, out var bucket);
            if (fresh) _buckets[limit.AppId] = bucket = new Bucket { Stamp = Stopwatch.GetTimestamp() };
            bucket!.Rate = Math.Max(1, limit.BytesPerSecond);
            bucket.Burst = (long)(bucket.Rate * _times.Burst.TotalSeconds);
            bucket.QueueLimit = Math.Max((long)(bucket.Rate * _times.Queue.TotalSeconds), PacketFilter.RecordSize); // a whole large packet always fits
            if (fresh) bucket.Tokens = bucket.Burst;
        }
    }

    private void Flush(Bucket bucket, bool strict)
    {
        while (bucket.Packets.Count > 0) SendHeld(bucket, strict);
    }

    /// Sends a program's oldest held packet. <paramref name="strict"/>: count a failure (while running), else just try (while stopping).
    private void SendHeld(Bucket bucket, bool strict)
    {
        var (data, length, cost) = bucket.Packets.Dequeue();
        bucket.Queued -= length;
        bucket.Tokens -= cost;
        try
        {
            if (_port is null) return; // held packets only exist while the driver is open; a closed one has let Windows deliver
            fixed (byte* p = data)
            {
                if (strict) Send(p, length);
                else _port.Send(p, length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(data); }
    }

    private void Send(byte* record, int length)
    {
        if (_port!.Send(record, length)) _sendFailures = 0;
        else if (++_sendFailures >= SendFailuresToStop) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// One program's limit: tokens are bytes it may receive now (they refill at <see cref="Rate"/> up to <see cref="Burst"/>; a packet goes
    /// while any are left and may take them below zero, so a large packet never waits for ever), and the packets held back meanwhile.
    private sealed class Bucket
    {
        public long Rate, Burst, QueueLimit, Queued, Stamp;
        public double Tokens;
        public readonly Queue<(byte[] Data, int Length, int Cost)> Packets = new();

        public void Refill(long now)
        {
            Tokens = Math.Min(Burst, Tokens + (now - Stamp) * (double)Rate / Stopwatch.Frequency);
            Stamp = now;
        }
    }
}

/// Hands one packet to the shaper: the driver's record (the packet plus whatever the driver needs to send it on), its length, where the IP
/// header starts in it (-1: not IP, sent straight on) and the IP packet's length (what the limit counts).
internal unsafe delegate void PacketHandler(byte* record, int length, int ipAt, int ipLength);
