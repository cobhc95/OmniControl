using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Resources;

public enum AdapterKind { Wifi, Ethernet, Vpn, Virtual, Other }

/// One network adapter as the sampler saw it: its stable id and name, what kind it is, whether it is connected, and its speed in bytes per second (0 when not connected).
public sealed record AdapterSpeed(string Id, string Name, AdapterKind Kind, bool IsUp, double Download, double Upload)
{
    /// The stored choice meaning "every adapter added together" (null means Auto, the busiest one).
    public const string All = "*";

    private static readonly string[] VpnWords = { "VPN", "TAP-", "WireGuard", "Tailscale", "ZeroTier", "TUN" };

    /// Wi-Fi, Ethernet or a physical card is "physical"; a VPN or a virtual switch port is not.
    public bool IsPhysical => Kind is AdapterKind.Wifi or AdapterKind.Ethernet;

    public static AdapterKind KindOf(NetworkInterfaceType type, string description, bool hardware)
    {
        if (type == NetworkInterfaceType.Ppp || VpnWords.Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase))) return AdapterKind.Vpn;
        if (!hardware) return AdapterKind.Virtual;
        return type switch
        {
            NetworkInterfaceType.Wireless80211 => AdapterKind.Wifi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                or NetworkInterfaceType.GigabitEthernet => AdapterKind.Ethernet,
            _ => AdapterKind.Other,
        };
    }
}

/// One interface as Windows' interface table (GetIfTable2) gives it: NDIS filter instance (QoS, Hyper-V extensions: never a real adapter)
/// or hardware, connected, and its byte counters since it started.
public readonly record struct AdapterRow(bool Hardware, bool Filter, bool Up, long Received, long Sent);

/// Windows' interface table, read in one call (GetIfTable2) per look: flags, state and counters of every interface by its id.
public static class AdapterTable
{
    // MIB_IF_ROW2 (x64): InterfaceGuid at 12, InterfaceAndOperStatusFlags at 1152, OperStatus at 1156, InOctets at 1208, OutOctets at 1280.
    private const int NetioSuccess = 0, RowSize = 1352, RowsOffset = 8, GuidOffset = 12, FlagsOffset = 1152, OperStatusOffset = 1156,
        InOctetsOffset = 1208, OutOctetsOffset = 1280, OperUp = 1;
    private const byte HardwareBit = 1, FilterBit = 2;

    /// Interface id (as <see cref="NetworkInterface.Id"/>) to its row; empty when Windows cannot list them, so nothing is hidden by mistake.
    public static Dictionary<string, AdapterRow> Read()
    {
        var result = new Dictionary<string, AdapterRow>(StringComparer.OrdinalIgnoreCase);
        if (GetIfTable2(out var table) != NetioSuccess) return result;
        try
        {
            var count = Marshal.ReadInt32(table);
            for (var i = 0; i < count; i++)
            {
                var row = table + RowsOffset + i * RowSize;
                var guid = Marshal.PtrToStructure<Guid>(row + GuidOffset);
                var flags = Marshal.ReadByte(row + FlagsOffset);
                result["{" + guid.ToString().ToUpperInvariant() + "}"] = new AdapterRow((flags & HardwareBit) != 0, (flags & FilterBit) != 0,
                    Marshal.ReadInt32(row + OperStatusOffset) == OperUp, Marshal.ReadInt64(row + InOctetsOffset), Marshal.ReadInt64(row + OutOctetsOffset));
            }
        }
        finally { FreeMibTable(table); }
        return result;
    }

    [DllImport("iphlpapi.dll")] private static extern int GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
}
