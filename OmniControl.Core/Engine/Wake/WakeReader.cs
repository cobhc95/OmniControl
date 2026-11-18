using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine.Wake;

/// <summary>Reads <see cref="WakeFacts"/> from Windows (network connections, the card's power management through WMI, the Fast Startup setting). Reading changes nothing. Call off the UI thread.</summary>
public static class WakeReader
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    public static WakeFacts Read()
    {
        var facts = new WakeFacts { ModernStandby = WakeNative.IsModernStandby(), FastStartupOn = FastStartup() };
        try
        {
            if (ActiveCard() is not { } card) return facts;
            facts.Adapter = card.Description;
            facts.Mac = string.Join(':', card.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            facts.Subnet = SubnetOf(card);
            ReadPower(card, facts);
            facts.Known = facts.Mac.Length > 0 && facts.Subnet.Length > 0;
        }
        catch (Exception ex) when (ex is NetworkInformationException or ManagementException or COMException or UnauthorizedAccessException)
        {
            facts.Known = false; // Windows would not say: shown as "not known", never as a guess
        }
        return facts;
    }

    private static bool FastStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
        return key?.GetValue("HiberbootEnabled") is int value && value != 0;
    }

    /// <summary>The wired or wireless card that is up and has an IPv4 gateway (the one the PC uses to reach its network).</summary>
    private static NetworkInterface? ActiveCard() => NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)));

    private static string SubnetOf(NetworkInterface card)
    {
        var unicast = card.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        if (unicast is null) return string.Empty;
        var address = unicast.Address.GetAddressBytes();
        var mask = unicast.IPv4Mask.GetAddressBytes();
        var network = address.Select((b, i) => (byte)(b & mask[i])).ToArray();
        return $"{new IPAddress(network)}/{mask.Sum(b => BitOperations.PopCount(b))}";
    }

    // The card's own wake settings (root\StandardCimv2: 2 = enabled, 1 = disabled, anything else = the driver does not offer it) and
    // Windows' permission for the card to wake the PC (root\wmi, keyed by the card's device id).
    private static void ReadPower(NetworkInterface card, WakeFacts facts)
    {
        using (var search = new ManagementObjectSearcher(@"root\StandardCimv2", $"SELECT * FROM MSFT_NetAdapterPowerManagementSettingData WHERE Name = '{Escape(card.Name)}'"))
        {
            foreach (var row in search.Get())
            {
                var magic = Level(row["WakeOnMagicPacket"]);
                var pattern = Level(row["WakeOnPattern"]);
                facts.CardCanWake = magic != 0;
                facts.MagicPacketOn = magic == 2;
                facts.MagicOnly = pattern != 2;
            }
        }
        var device = PnpId(card.Id);
        if (device.Length == 0) return;
        using var wake = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Enable FROM MSPower_DeviceWakeEnable");
        foreach (var row in wake.Get())
            if (row["InstanceName"] is string name && name.StartsWith(device, StringComparison.OrdinalIgnoreCase)) facts.Armed = row["Enable"] is true;
    }

    private static int Level(object? value) => value switch
    {
        string text when text.Equals("Enabled", StringComparison.OrdinalIgnoreCase) => 2,
        string text when text.Equals("Disabled", StringComparison.OrdinalIgnoreCase) => 1,
        null => 0,
        _ => int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var number) ? number : 0,
    };

    /// <summary>The card's plug-and-play device id (what the wake permission is keyed by), from its connection id.</summary>
    private static string PnpId(string connectionId)
    {
        using var search = new ManagementObjectSearcher($"SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE GUID = '{Escape(connectionId)}'");
        foreach (var row in search.Get())
            if (row["PNPDeviceID"] is string id) return id;
        return string.Empty;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("'", "\\'");
}
