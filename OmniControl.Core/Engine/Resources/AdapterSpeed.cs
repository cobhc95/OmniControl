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

    public static AdapterKind KindOf(NetworkInterface nic, bool hardware)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ppp || VpnWords.Any(w => nic.Description.Contains(w, StringComparison.OrdinalIgnoreCase))) return AdapterKind.Vpn;
        if (!hardware) return AdapterKind.Virtual;
        return nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => AdapterKind.Wifi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                or NetworkInterfaceType.GigabitEthernet => AdapterKind.Ethernet,
            _ => AdapterKind.Other,
        };
    }
}

/// Windows' own flags per interface (GetIfTable2): which ones are NDIS filter instances (QoS, Hyper-V extensions: never a real adapter) and which are hardware.
public static class AdapterFlags
{
    private const int NetioSuccess = 0, RowSize = 1352, RowsOffset = 8, GuidOffset = 12, FlagsOffset = 1152;
    private const byte HardwareBit = 1, FilterBit = 2;

    /// Interface id (as <see cref="NetworkInterface.Id"/>) to (hardware, filter); empty when Windows cannot list them, so nothing is hidden by mistake.
    public static Dictionary<string, (bool Hardware, bool Filter)> Read()
    {
        var result = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
        if (GetIfTable2(out var table) != NetioSuccess) return result;
        try
        {
            var count = Marshal.ReadInt32(table);
            for (var i = 0; i < count; i++)
            {
                var row = table + RowsOffset + i * RowSize;
                var guid = Marshal.PtrToStructure<Guid>(row + GuidOffset);
                var flags = Marshal.ReadByte(row + FlagsOffset);
                result["{" + guid.ToString().ToUpperInvariant() + "}"] = ((flags & HardwareBit) != 0, (flags & FilterBit) != 0);
            }
        }
        finally { FreeMibTable(table); }
        return result;
    }

    [DllImport("iphlpapi.dll")] private static extern int GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
}
