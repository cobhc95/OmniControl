using Microsoft.Win32.SafeHandles;
using static OmniControl.Core.Native.DeviceIoNative;

namespace OmniControl.Core.Engine.Resources;

/// Windows Packet Filter (WinpkFilter, the NDIS lightweight filter ndisrd.sys installed system-wide, <see cref="PacketFilterSetup"/>)
/// as the shaper's packet port, through its device \\.\NDISRD and the IOCTLs and packed structs of ndisapi's Common.h. While open, every
/// Ethernet-style adapter (not WinTun, <see cref="Refresh"/>) tunnels received frames (MSTCP_FLAG_RECV_TUNNEL), but the driver's static filter
/// table hands OmniControl only received TCP and UDP packets and passes the rest itself (<see cref="Redirect"/>); the shaper sends each
/// handed-over frame on to Windows' stack. Closing
/// sets every adapter back to mode 0, clears the table, sends on what is still queued and closes the handle (the driver also lets go of an
/// adapter's mode when the handle closes, so a crash does not leave traffic held). A record is the INTERMEDIATE_BUFFER: its header, then the frame.
/// Used by the shaper's thread only; Dispose never throws.
internal sealed unsafe class PacketFilter : IDisposable
{
    private const string Device = @"\\.\NDISRD";
    private const uint IoctlBase = 0x8300u << 16, IndexBase = 0x830; // CTL_CODE(FILE_DEVICE_NDISRD, NDISRD_IOCTL_INDEX + n, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint GetAdapters = 1, SendToStack = 3, SetMode = 5, SetEvent = 7, SetFilters = 14, ResetFilters = 15, ReadMany = 22;
    private const uint RecvTunnel = 0x2; // MSTCP_FLAG_RECV_TUNNEL
    // STATIC_FILTER under pack(1) (184 bytes, after the table's count and padding): adapter (0 = all) 0, direction 8, action 12, valid layers 16,
    // network selector 80, its IPv4/IPv6 filter's valid fields 84 and protocol 112 / 160, transport selector 164, its valid fields 168, destination port range 176.
    private const int TableHeader = 8, FilterSize = 184;
    private const uint OnSend = 1, OnReceive = 2, Pass = 1, Redirected = 3, NetworkLayer = 2, TransportLayer = 4, Ipv4 = 1, Ipv6 = 2, ProtocolField = 4, TcpUdp = 1, DestPort = 2;
    // TCP_AdapterList under pack(1): count, 32 names of 256 bytes, 32 handles, 32 mediums (NDIS_MEDIUM), addresses, MTUs.
    private const int AdapterListSize = 8836, AdapterNameSize = 256, AdapterHandlesAt = 4 + 32 * AdapterNameSize, AdapterMediumsAt = AdapterHandlesAt + 32 * 8, MaxAdapters = 32;
    private const uint MediumIp = 19; // NdisMediumIP: WinTun tunnels (Tailscale, WireGuard), whose frames carry no Ethernet header
    // INTERMEDIATE_BUFFER under pack(1): the adapter / list link (16), device flags, length at 20, flags, 802.1q, filter id, 4 reserved, the frame.
    private const int HeaderSize = 52, LengthAt = 20, MaxFrame = 1514;

    /// The largest record, so a program's queue always holds one.
    public const int RecordSize = HeaderSize + MaxFrame;

    private const int Batch = 64; // frames per read (ETH_M_REQUEST: adapter, count, done, then one buffer pointer each)
    private const int Ethernet = 14, VlanTag = 4, EtherIpv4 = 0x0800, EtherIpv6 = 0x86DD, EtherVlan = 0x8100;

    private readonly SafeFileHandle _device;
    private readonly ManualResetEvent _arrived = new(false);
    private readonly List<IntPtr> _adapters = new();
    private readonly Dictionary<IntPtr, string> _ids = new(); // each adapter handle's id ("{GUID}"), from the driver's name for it
    private readonly byte* _records = (byte*)NativeMemory.Alloc(Batch * RecordSize);
    private readonly byte* _request = (byte*)NativeMemory.Alloc(16 + Batch * 8);
    private readonly byte* _send = (byte*)NativeMemory.Alloc(RecordSize);

    private PacketFilter(SafeFileHandle device) => _device = device;

    /// The driver's service under HKLM (<see cref="PacketFilterSetup"/>'s receipt is written only once it is there).
    internal const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\ndisrd";

    /// True when the driver is installed: its file is in Windows' drivers folder and its service is registered (removing the filter
    /// may leave the file behind); nothing is opened.
    public static bool Installed
    {
        get
        {
            using var service = Registry.LocalMachine.OpenSubKey(ServiceKey);
            return service is not null && File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "ndisrd.sys"));
        }
    }

    /// Opens the driver and tunnels every adapter's incoming frames; throws a <see cref="Win32Exception"/> whose message is one plain sentence
    /// when it cannot (nothing is left changed then).
    public static PacketFilter Open()
    {
        var device = CreateFile(Device, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (device.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            throw new Win32Exception(error, Says(error));
        }
        var port = new PacketFilter(device);
        try
        {
            port.Redirect(); // the table first, so tunnelling never hands over other frames, not even for a moment
            port.Refresh();
        }
        catch (Win32Exception ex)
        {
            port.Dispose();
            throw new Win32Exception(ex.NativeErrorCode, Says(ex.NativeErrorCode));
        }
        return port;
    }

    /// Set when packets may be waiting.
    public WaitHandle Arrived => _arrived;

    /// Reads the adapter list again: every adapter the driver is bound to with Ethernet-style frames (Wi-Fi, Ethernet, TAP-style VPNs) gets
    /// the one "packets waiting" event and tunnels its received frames, so a limit holds on whichever of them a program's traffic arrives; an
    /// adapter that has gone is forgotten. IP-medium tunnels (WinTun) are left alone: <see cref="IpAt"/> cannot read their frames, so
    /// tunnelling them shapes nothing and only puts their traffic at risk (Tailscale peers became unreachable).
    public void Refresh()
    {
        var now = new List<IntPtr>();
        var list = (byte*)NativeMemory.AllocZeroed(AdapterListSize);
        try
        {
            if (!Control(GetAdapters, list, AdapterListSize, list, AdapterListSize)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var count = Math.Min(*(int*)list, MaxAdapters);
            for (var i = 0; i < count; i++)
                if (((IntPtr*)(list + AdapterHandlesAt))[i] is var adapter && adapter != IntPtr.Zero && ((uint*)(list + AdapterMediumsAt))[i] != MediumIp)
                {
                    now.Add(adapter);
                    _ids[adapter] = IdOf(new string((sbyte*)(list + 4 + i * AdapterNameSize)));
                }
        }
        finally { NativeMemory.Free(list); }
        _adapters.RemoveAll(adapter => !now.Contains(adapter));
        foreach (var gone in _ids.Keys.Where(a => !now.Contains(a)).ToList()) _ids.Remove(gone);
        var eventHandle = _arrived.SafeWaitHandle.DangerousGetHandle();
        foreach (var adapter in now.Except(_adapters).ToList())
        {
            _adapters.Add(adapter); // listed first, so closing sets it back even if only half of this worked
            if (!Pair(SetEvent, adapter, eventHandle) || !Mode(adapter, RecvTunnel)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// Loads the driver's static filter table: every received TCP or UDP packet (IPv4 and IPv6) is redirected to OmniControl, since a new
    /// connection's port is only known once Windows lists it; the last filter passes everything else inside the driver.
    private void Redirect()
    {
        var ranges = new[] { (PortOwners.Tcp, 0, ushort.MaxValue), (PortOwners.Udp, 0, ushort.MaxValue) };
        var count = ranges.Length * 2 + 1;
        var size = TableHeader + count * FilterSize;
        var table = (byte*)NativeMemory.AllocZeroed((nuint)size);
        try
        {
            *(int*)table = count;
            var filter = table + TableHeader;
            foreach (var (protocol, first, last) in ranges)
                foreach (var ip in new[] { Ipv4, Ipv6 })
                {
                    *(uint*)(filter + 8) = OnReceive;
                    *(uint*)(filter + 12) = Redirected;
                    *(uint*)(filter + 16) = NetworkLayer | TransportLayer;
                    *(uint*)(filter + 80) = ip;
                    *(uint*)(filter + 84) = ProtocolField;
                    filter[ip == Ipv4 ? 112 : 160] = (byte)protocol;
                    *(uint*)(filter + 164) = TcpUdp;
                    *(uint*)(filter + 168) = DestPort;
                    *(ushort*)(filter + 176) = (ushort)first;
                    *(ushort*)(filter + 178) = (ushort)last;
                    filter += FilterSize;
                }
            *(uint*)(filter + 8) = OnSend | OnReceive;
            *(uint*)(filter + 12) = Pass;
            if (!Control(SetFilters, table, size, null, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { NativeMemory.Free(table); }
    }

    /// Hands every packet waiting now to <paramref name="pass"/>; throws a <see cref="Win32Exception"/> when the driver fails.
    public void Take(PacketHandler pass)
    {
        _arrived.Reset(); // before reading, so a frame queued after a read finds the event set again
        foreach (var adapter in _adapters)
        {
            int read;
            do
            {
                read = Read(adapter);
                for (var i = 0; i < read; i++)
                {
                    var record = _records + i * RecordSize;
                    *(IntPtr*)record = adapter; // what Send needs to name the adapter, also for a held copy
                    var frame = Math.Min(*(int*)(record + LengthAt), MaxFrame);
                    var ipAt = IpAt(record + HeaderSize, frame);
                    pass(record, HeaderSize + frame, ipAt < 0 ? -1 : HeaderSize + ipAt, frame - ipAt);
                }
            }
            while (read == Batch);
        }
    }

    /// Up to <see cref="Batch"/> frames waiting on one adapter; 0 when none (the driver then says no).
    private int Read(IntPtr adapter)
    {
        *(IntPtr*)_request = adapter;
        *(uint*)(_request + 8) = Batch;
        *(uint*)(_request + 12) = 0;
        for (var i = 0; i < Batch; i++) ((IntPtr*)(_request + 16))[i] = (IntPtr)(_records + i * RecordSize);
        const int size = 16 + Batch * 8;
        return Control(ReadMany, _request, size, _request, size) ? (int)Math.Min(*(uint*)(_request + 12), Batch) : 0;
    }

    /// The id ("{GUID}") of the adapter a record came on, or null when the driver did not name it.
    public string? AdapterOf(byte* record) => _ids.GetValueOrDefault(*(IntPtr*)record);

    /// The driver names an adapter "\DEVICE\{GUID}": its id is the last part, in capitals as <c>NetworkInterface.Id</c>.
    private static string IdOf(string name) => name[(name.LastIndexOf('\\') + 1)..].ToUpperInvariant();

    /// Sends a record on to Windows' network stack; false (last error set) when the driver refused it.
    public bool Send(byte* record, int length)
    {
        // The driver reads a whole INTERMEDIATE_BUFFER, so a held copy (only as long as its frame) goes through a full-size buffer.
        Buffer.MemoryCopy(record, _send, RecordSize, Math.Min(length, RecordSize));
        return Pair(SendToStack, *(IntPtr*)record, (IntPtr)_send);
    }

    /// Where the IP header starts in an Ethernet frame (after one 802.1Q tag if there is one), or -1 for anything else (ARP...).
    private static int IpAt(byte* frame, int length)
    {
        if (length < Ethernet) return -1;
        var at = Ethernet;
        var type = frame[12] << 8 | frame[13];
        if (type == EtherVlan && length >= Ethernet + VlanTag)
        {
            type = frame[16] << 8 | frame[17];
            at += VlanTag;
        }
        return type is EtherIpv4 or EtherIpv6 ? at : -1;
    }

    public void Dispose()
    {
        // Best effort, each step on its own: the adapters go back to mode 0, the filter table is cleared, what the driver still holds goes on,
        // then the handle closes.
        foreach (var adapter in _adapters) Mode(adapter, 0);
        Control(ResetFilters, null, 0, null, 0);
        try
        {
            Take((record, length, _, _) => Send(record, length));
        }
        catch (Win32Exception)
        {
            // Nothing more can be read; closing the handle lets the driver drop its queue.
        }
        _device.Dispose();
        _arrived.Dispose();
        NativeMemory.Free(_records);
        NativeMemory.Free(_request);
        NativeMemory.Free(_send);
    }

    private bool Mode(IntPtr adapter, uint flags)
    {
        var mode = stackalloc byte[12]; // ADAPTER_MODE: the adapter, then its flags
        *(IntPtr*)mode = adapter;
        *(uint*)(mode + 8) = flags;
        return Control(SetMode, mode, 12, null, 0);
    }

    /// An IOCTL whose input is two handle-sized values (ADAPTER_EVENT, or ETH_REQUEST: the adapter and a buffer).
    private bool Pair(uint code, IntPtr first, IntPtr second)
    {
        var pair = stackalloc IntPtr[2] { first, second };
        return Control(code, pair, 16, null, 0);
    }

    private bool Control(uint code, void* input, int inputSize, void* output, int outputSize) =>
        DeviceIoControl(_device, IoctlBase | (IndexBase + code) << 2, input, inputSize, output, outputSize, out _, IntPtr.Zero);

    /// One plain sentence for a Windows error the driver gave.
    public static string Says(int error) => error switch
    {
        2 or 3 => "Windows Packet Filter's driver is not running (restart Windows)",
        5 => "Windows Packet Filter's driver needs administrator rights",
        _ => new Win32Exception(error).Message.TrimEnd('.'),
    };
}
