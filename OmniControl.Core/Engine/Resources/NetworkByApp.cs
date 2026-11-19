namespace OmniControl.Core.Engine.Resources;

/// What one program sent and received since the previous <see cref="NetworkByApp.Take"/>: its identity (<see cref="ProgramIdentity.Normalize"/>), its executable's path, and bytes.
public sealed record AppTraffic(string Id, string Path, long Received, long Sent);

/// Network bytes per program, from Windows' own kernel network events (the Microsoft-Windows-Kernel-Network ETW provider, TCP and UDP,
/// IPv4 and IPv6) in a real-time session of its own. Needs administrator rights (the app has them). The event callback only adds bytes up;
/// <see cref="Take"/> hands the totals over and starts again. <see cref="Dispose"/> stops the session; a session left by a crashed run
/// (same name) is stopped before a new one starts, so none is ever left behind for long.
public sealed class NetworkByApp : IDisposable
{
    private const string SessionName = "OmniControl-NetworkByApp";
    private static readonly Guid Provider = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private const ulong KeywordIpv4 = 0x10, KeywordIpv6 = 0x20;
    private const int Sent = 1, Received = 2;

    private readonly Dictionary<int, long[]> _bytes = new(); // process id -> [_, sent, received]
    private readonly ProcessPrograms _programs = new();
    private readonly EventCallback _callback; // kept in a field: Windows calls it for as long as the session runs
    private ulong _session, _trace = InvalidTrace;
    private Thread? _reader;
    private volatile string? _stopped;

    private NetworkByApp() => _callback = OnEvent;

    /// Null while Windows hands the events over; one plain sentence once the reader ended on its own (the session was stopped or failed).
    public string? Stopped => _stopped;

    /// Starts the session; throws a <see cref="Win32Exception"/> with Windows' reason when it cannot.
    public static NetworkByApp Start()
    {
        var watcher = new NetworkByApp();
        var started = false;
        try
        {
            watcher.Open();
            started = true;
            return watcher;
        }
        finally
        {
            if (!started) watcher.Dispose(); // a half-started session is stopped before the failure goes on to the caller
        }
    }

    /// The bytes of every program since the last call, joined by program (one entry per program, busiest first).
    public IReadOnlyList<AppTraffic> Take()
    {
        List<(int Pid, long Sent, long Received)> taken;
        lock (_bytes)
        {
            taken = _bytes.Select(p => (p.Key, p.Value[Sent], p.Value[Received])).ToList();
            _bytes.Clear();
        }
        var traffic = taken.Select(t => (Program: _programs.Of(t.Pid), t.Sent, t.Received)).Where(t => t.Program.Id.Length > 0)
            .GroupBy(t => t.Program.Id).Select(g => new AppTraffic(g.Key, g.First().Program.Path, g.Sum(t => t.Received), g.Sum(t => t.Sent)))
            .OrderByDescending(a => a.Received + a.Sent).ToList();
        _programs.Keep(taken.Select(t => t.Pid).ToHashSet());
        return traffic;
    }

    private void Open()
    {
        var properties = Properties();
        try
        {
            var error = StartTrace(out _session, SessionName, properties);
            if (error == AlreadyExists)
            {
                ControlTrace(0, SessionName, properties, ControlStop); // a crashed run's session
                properties = Reset(properties);
                error = StartTrace(out _session, SessionName, properties);
            }
            Check(error);
            var provider = Provider;
            Check(EnableTraceEx2(_session, ref provider, EnableProvider, LevelInformation, KeywordIpv4 | KeywordIpv6, 0, 0, IntPtr.Zero));
        }
        finally { Marshal.FreeHGlobal(properties); }

        var logfile = new TraceLogfile { LoggerName = SessionName, ProcessTraceMode = RealTime | EventRecord, Callback = _callback };
        _trace = OpenTrace(ref logfile);
        if (_trace == InvalidTrace) throw new Win32Exception(Marshal.GetLastWin32Error());
        _reader = new Thread(Read) { IsBackground = true, Name = "NetworkByApp" };
        _reader.Start();
    }

    private void Read()
    {
        var error = ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero);
        if (_session != 0) _stopped = "Windows stopped reporting traffic per program" + (error != 0 ? $": {new Win32Exception(error).Message}" : string.Empty);
    }

    private static void Check(int error)
    {
        if (error != 0) throw new Win32Exception(error);
    }

    private void OnEvent(IntPtr record)
    {
        if (!TryRead(record, out var pid, out var size, out var sent)) return;
        lock (_bytes)
        {
            if (!_bytes.TryGetValue(pid, out var bytes)) _bytes[pid] = bytes = new long[3];
            bytes[sent ? Sent : Received] += size;
        }
    }

    /// One EVENT_RECORD (x64): the header's event id (offset 40) tells sent from received; the payload (pointer at 96, length at 86) starts
    /// with the process id and the size for every event used. False for any other event.
    internal static bool TryRead(IntPtr record, out int pid, out int size, out bool sent)
    {
        (pid, size) = (0, 0);
        var id = (ushort)Marshal.ReadInt16(record, 40);
        sent = id is 10 or 26 or 42 or 58; // TCP and UDP, IPv4 and IPv6
        if ((!sent && id is not (11 or 27 or 43 or 59)) || (ushort)Marshal.ReadInt16(record, 86) < 8) return false;
        var data = Marshal.ReadIntPtr(record, 96);
        (pid, size) = (Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
        return true;
    }

    public void Dispose()
    {
        if (_session != 0)
        {
            var properties = Properties();
            ControlTrace(_session, null, properties, ControlStop);
            Marshal.FreeHGlobal(properties);
            _session = 0;
        }
        if (_trace != InvalidTrace) CloseTrace(_trace);
        _trace = InvalidTrace;
        _reader?.Join(CoreTiming.NetworkByAppStopWait);
    }

    // ---- ETW ----

    private const int AlreadyExists = 183, ControlStop = 1, EnableProvider = 1;
    private const byte LevelInformation = 4;
    private const uint RealTime = 0x100, EventRecord = 0x10000000, TracedGuid = 0x20000, QpcClock = 1;
    private const ulong InvalidTrace = ulong.MaxValue;
    private static readonly int PropertiesSize = Marshal.SizeOf<TraceProperties>(), NameRoom = 1024 * 2;

    private static IntPtr Properties() => Reset(Marshal.AllocHGlobal(PropertiesSize + NameRoom));

    /// A real-time session's properties, with room for its name after them (where Windows writes it). Without a flush timer Windows hands
    /// a real-time consumer a buffer only once it is full (one per processor), so the per-program bytes arrived minutes late or never.
    private static IntPtr Reset(IntPtr memory)
    {
        var zero = new byte[PropertiesSize + NameRoom];
        Marshal.Copy(zero, 0, memory, zero.Length);
        Marshal.StructureToPtr(new TraceProperties
        {
            BufferSizeTotal = (uint)(PropertiesSize + NameRoom), Flags = TracedGuid, ClientContext = QpcClock, LogFileMode = RealTime,
            FlushTimer = CoreTiming.NetworkByAppFlushSeconds, LoggerNameOffset = (uint)PropertiesSize,
        }, memory, false);
        return memory;
    }

    private delegate void EventCallback(IntPtr record);

    [StructLayout(LayoutKind.Sequential)]
    private struct TraceProperties
    {
        public uint BufferSizeTotal, ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext, Flags, BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }

    // EVENT_TRACE_LOGFILEW (x64): the current event and the log header are only room Windows fills; nothing here reads them.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TraceLogfile
    {
        public IntPtr LogFileName;
        public string LoggerName;
        public long CurrentTime;
        public uint BuffersRead, ProcessTraceMode;
        public Room88 CurrentEvent;
        public Room280 LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize, Filled, EventsLost;
        public EventCallback Callback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential, Size = 88)] private struct Room88 { private long _first; }
    [StructLayout(LayoutKind.Sequential, Size = 280)] private struct Room280 { private long _first; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int StartTrace(out ulong session, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int ControlTrace(ulong session, string? name, IntPtr properties, int code);
    [DllImport("advapi32.dll")] private static extern int EnableTraceEx2(ulong session, ref Guid provider, int code, byte level, ulong any, ulong all, int timeout, IntPtr parameters);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ulong OpenTrace(ref TraceLogfile logfile);
    [DllImport("advapi32.dll")] private static extern int ProcessTrace(ulong[] handles, int count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] private static extern int CloseTrace(ulong trace);
}
