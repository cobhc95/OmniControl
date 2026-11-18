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

    private const string AdapterClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    /// <summary>A part of the read failed (text for the log), raised once per distinct text, on the calling thread.</summary>
    public static event Action<string>? Failed;

    private static readonly HashSet<string> Told = new();

    public static WakeFacts Read()
    {
        var facts = new WakeFacts { ModernStandby = WakeNative.IsModernStandby(), FastStartupOn = FastStartup() };
        NetworkInterface? card;
        try { card = ActiveCard(); }
        catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException)
        {
            Tell("network cards", ex);
            return facts;
        }
        if (card is null) return facts;
        facts.Adapter = card.Description;
        var addressRead = Guarded("card address", () => facts.Mac = string.Join(':', card.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))));
        var subnetRead = Guarded("card subnet", () => facts.Subnet = SubnetOf(card));
        facts.Known = addressRead && subnetRead && facts.Mac.Length > 0 && facts.Subnet.Length > 0;
        if (!facts.Known) return facts;

        // Each source may fail on its own (one broken adapter fails the whole WMI class); the card's settings are known if any source answers.
        var settings = Guarded("card power settings (WMI)", () => ReadSettingsWmi(card, facts));
        if (!settings) settings = Guarded("card power settings (registry)", () => ReadSettingsRegistry(card, facts));
        var permission = Guarded("wake permission", () => ReadPermission(card, facts));
        if (!settings && permission) facts.CardCanWake = true; // Windows lists the card as wake-programmable
        if (!settings && !permission) facts.Known = false; // no source could say: shown as "not known", never as a guess
        return facts;
    }

    private static bool Guarded(string what, Func<bool> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is NetworkInformationException or ManagementException or COMException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Tell(what, ex);
            return false;
        }
    }

    private static bool Guarded(string what, Action read) => Guarded(what, () => { read(); return true; });

    private static void Tell(string what, Exception ex)
    {
        var text = $"Wake: could not read the {what}: {ex.Message}";
        lock (Told) if (!Told.Add(text)) return;
        Failed?.Invoke(text);
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

    // The card's own wake settings through WMI (root\StandardCimv2: 2 = enabled, 1 = disabled, anything else = the driver does not offer it).
    // False when the class gave no row for this card.
    private static bool ReadSettingsWmi(NetworkInterface card, WakeFacts facts)
    {
        var found = false;
        using var search = new ManagementObjectSearcher(@"root\StandardCimv2", $"SELECT * FROM MSFT_NetAdapterPowerManagementSettingData WHERE Name = '{Escape(card.Name)}'");
        foreach (var row in search.Get())
        {
            ApplySettings(Level(row["WakeOnMagicPacket"]), Level(row["WakeOnPattern"]), facts);
            found = true;
        }
        return found;
    }

    // The same settings from the driver's own keys (standardized keywords: "1" on, "0" off, missing = not offered), found by the card's id.
    private static bool ReadSettingsRegistry(NetworkInterface card, WakeFacts facts)
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(AdapterClassKey);
        if (classKey is null) return false;
        foreach (var name in classKey.GetSubKeyNames())
        {
            using var key = classKey.OpenSubKey(name);
            if (key?.GetValue("NetCfgInstanceId") is not string id || !id.Equals(card.Id, StringComparison.OrdinalIgnoreCase)) continue;
            ApplySettings(KeywordLevel(key.GetValue("*WakeOnMagicPacket")), KeywordLevel(key.GetValue("*WakeOnPattern")), facts);
            return true;
        }
        return false;
    }

    private static int KeywordLevel(object? value) => value is null ? 0 : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() switch { "1" => 2, "0" => 1, _ => 0 };

    private static void ApplySettings(int magic, int pattern, WakeFacts facts)
    {
        facts.CardCanWake = magic != 0;
        facts.MagicPacketOn = magic == 2;
        facts.MagicOnly = pattern != 2;
    }

    // Windows' permission for the card to wake the PC (root\wmi, keyed by the card's device id). True when the card has a row there.
    private static bool ReadPermission(NetworkInterface card, WakeFacts facts)
    {
        var device = PnpId(card.Id);
        if (device.Length == 0) return false;
        var found = false;
        using var wake = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Enable FROM MSPower_DeviceWakeEnable");
        foreach (var row in wake.Get())
            if (row["InstanceName"] is string name && name.StartsWith(device, StringComparison.OrdinalIgnoreCase))
            {
                facts.Armed = row["Enable"] is true;
                found = true;
            }
        return found;
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
