namespace OmniControl.Core.Engine.Resources;

/// Which process owns each local TCP and UDP port (IPv4 and IPv6), from Windows' own tables (GetExtendedTcpTable, GetExtendedUdpTable).
/// An incoming packet's destination port is such a local port, so this names the program it is for.
internal static class PortOwners
{
    public const int Tcp = 6, Udp = 17;
    private const int Ipv4 = 2, Ipv6 = 23, TcpOwnerPidAll = 5, UdpOwnerPid = 1, InsufficientBuffer = 122, Attempts = 4;

    /// The key of a port in <see cref="Fill"/>'s table.
    public static int Key(int protocol, int port) => protocol << 16 | port;

    /// Refills <paramref name="owners"/> (<see cref="Key"/> -> process id); throws a <see cref="Win32Exception"/> when Windows won't say.
    public static void Fill(Dictionary<int, int> owners)
    {
        owners.Clear();
        // Row sizes and where the port and the process id sit in MIB_TCPROW_OWNER_PID, MIB_TCP6ROW_OWNER_PID, MIB_UDPROW_OWNER_PID, MIB_UDP6ROW_OWNER_PID.
        Add(owners, Tcp, Ipv4, 24, 8, 20);
        Add(owners, Tcp, Ipv6, 56, 20, 52);
        Add(owners, Udp, Ipv4, 12, 4, 8);
        Add(owners, Udp, Ipv6, 28, 20, 24);
    }

    private static void Add(Dictionary<int, int> owners, int protocol, int family, int rowSize, int portAt, int pidAt)
    {
        var size = 0;
        var table = IntPtr.Zero;
        try
        {
            uint error = InsufficientBuffer;
            for (var attempt = 0; attempt < Attempts && error == InsufficientBuffer; attempt++)
            {
                if (size > 0)
                {
                    Marshal.FreeHGlobal(table);
                    table = Marshal.AllocHGlobal(size);
                }
                error = protocol == Tcp ? GetExtendedTcpTable(table, ref size, false, family, TcpOwnerPidAll, 0)
                    : GetExtendedUdpTable(table, ref size, false, family, UdpOwnerPid, 0);
            }
            if (error != 0) throw new Win32Exception((int)error);
            if (table == IntPtr.Zero) return;
            var rows = Marshal.ReadInt32(table);
            for (var i = 0; i < rows; i++)
            {
                var row = table + 4 + i * rowSize;
                var pid = Marshal.ReadInt32(row, pidAt);
                var raw = Marshal.ReadInt32(row, portAt); // the port in network byte order in the low two bytes
                if (pid != 0) owners[Key(protocol, (raw & 0xFF) << 8 | (raw >> 8 & 0xFF))] = pid; // 0: a closed connection waiting out its time
            }
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
}
