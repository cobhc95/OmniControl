using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OmniControl.Core;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine.Wake;

/// <summary>Reads <see cref="WakeFacts"/> from Windows (network connections, the card's power management through WMI, the Fast Startup setting). Reading changes nothing. Call off the UI thread.</summary>
public static class WakeReader
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    private const string AdapterClassKey = @"SYSTEM\CurrentControlSet\Control\Class\" + WakeKeywords.AdapterClass;

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

        // Every fact is a chain of sources tried in order; each runs on its own thread with its own wait (a slow one is given up on and the next is tried),
        // each may fail on its own (one broken adapter fails a whole WMI class), and a fact is unknown only when all fail.
        var magic = 0;
        var pattern = 0;
        var settingsSource = "none";
        var wmi = Bounded("card power settings (WMI)", CoreTiming.WakeReadWmiMs, () => ReadSettingsWmi(card));
        if (wmi.Found)
        {
            (magic, pattern) = (wmi.Magic, wmi.Pattern);
            settingsSource = "WMI";
        }
        if (magic == 0 || pattern == 0)
        {
            var fromKey = Bounded("card power settings (registry)", CoreTiming.WakeReadApiMs, () => ReadSettingsRegistry(card));
            if (fromKey.Found && (magic == 0 && fromKey.Magic != 0 || pattern == 0 && fromKey.Pattern != 0))
            {
                settingsSource = settingsSource == "WMI" ? "WMI + driver registry" : "driver registry";
                if (magic == 0) magic = fromKey.Magic;
                if (pattern == 0) pattern = fromKey.Pattern;
            }
        }
        facts.MagicPacketOn = magic == 2;
        facts.MagicOnly = pattern != 2;

        // Windows' device power list leaves out some cards that powercfg shows, so a name found by either says yes, and "no" needs a list that answered.
        var canWake = false;
        var canWakeSource = "none";
        var armed = false;
        var armedAnswered = false;
        var armedSource = "none";
        var programmable = Bounded("wake-capable list (powercfg)", CoreTiming.WakeReadPowercfgMs, () => DevicePowerNative.PowercfgWakeProgrammable(CoreTiming.WakeReadPowercfgMs));
        if (programmable is not null && Names(programmable, card)) (canWake, canWakeSource) = (true, "powercfg wake_programmable");
        var apiProgrammable = canWake ? null : Bounded("wake-capable list (device power)", CoreTiming.WakeReadApiMs, DevicePowerNative.WakeProgrammableNames);
        if (apiProgrammable is not null && Names(apiProgrammable, card)) (canWake, canWakeSource) = (true, "Windows device power list");
        var enabled = Bounded("wake-armed list (powercfg)", CoreTiming.WakeReadPowercfgMs, () => DevicePowerNative.PowercfgWakeArmed(CoreTiming.WakeReadPowercfgMs));
        if (enabled is not null) (armed, armedAnswered, armedSource) = (Names(enabled, card), true, "powercfg wake_armed");
        if (!armed)
        {
            var apiEnabled = Bounded("wake-armed list (device power)", CoreTiming.WakeReadApiMs, DevicePowerNative.WakeEnabledNames);
            if (apiEnabled is not null)
            {
                if (Names(apiEnabled, card)) (armed, armedSource) = (true, "Windows device power list");
                else if (!armedAnswered) armedSource = "Windows device power list";
                armedAnswered = true;
            }
        }
        var row = Bounded("wake permission (WMI)", CoreTiming.WakeReadWmiMs, () => ReadPermission(card));
        if (row.Found)
        {
            if (!canWake) (canWake, canWakeSource) = (true, "WMI wake row");
            if (!armed) (armed, armedAnswered, armedSource) = (row.Enabled, true, armedAnswered && !row.Enabled ? armedSource : "WMI wake row");
        }
        if (!canWake && magic != 0) (canWake, canWakeSource) = (true, settingsSource + " (wake keywords)");

        facts.CardCanWake = canWake;
        facts.Armed = armed;
        if (!canWake && !armedAnswered && settingsSource == "none" && !row.Found) facts.Known = false; // no source could say: shown as "not known", never as a guess
        Say($"Wake: can wake = {canWake} (from {canWakeSource}); armed = {armed} (from {armedSource}); magic packet = {facts.MagicPacketOn}, magic only = {facts.MagicOnly} (from {settingsSource})");
        return facts;
    }

    private readonly record struct SettingsRead(bool Found, int Magic, int Pattern);

    private readonly record struct PermissionRead(bool Found, bool Enabled);

    /// <summary>One line per read saying which source answered each fact (raised once per distinct text), for the log.</summary>
    public static event Action<string>? Answered;

    private static void Say(string text)
    {
        lock (Told) if (!Told.Add(text)) return;
        Answered?.Invoke(text);
    }

    /// <summary>The card's name as a device in one of Windows' wake lists (the list holds the friendly name, which is the card's interface description).</summary>
    private static bool Names(HashSet<string> list, NetworkInterface card) => list.Contains(card.Description) || list.Contains(card.Name);

    /// <summary>Runs one source off the calling thread and waits at most <paramref name="waitMs"/>; a source that is slower or fails gives its default (a source is never waited on twice).</summary>
    private static T? Bounded<T>(string what, int waitMs, Func<T> read)
    {
        var task = Task.Run(() =>
        {
            T? value = default;
            Guarded(what, () => { value = read(); });
            return value;
        });
        if (task.Wait(waitMs)) return task.Result;
        Tell(what, new TimeoutException($"it took longer than {waitMs / 1000} s, so the next source was used"));
        return default;
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
    private static SettingsRead ReadSettingsWmi(NetworkInterface card)
    {
        var found = false;
        var magic = 0;
        var pattern = 0;
        using var search = new ManagementObjectSearcher(@"root\StandardCimv2", $"SELECT * FROM MSFT_NetAdapterPowerManagementSettingData WHERE Name = '{Escape(card.Name)}'");
        foreach (var row in search.Get())
        {
            magic = Level(row["WakeOnMagicPacket"]);
            pattern = Level(row["WakeOnPattern"]);
            found = true;
        }
        return new SettingsRead(found, magic, pattern);
    }

    // The same settings from the driver's own keys, found by the card's id: the standard keywords plus vendor ones found by their description.
    // True when the card's key was found.
    private static SettingsRead ReadSettingsRegistry(NetworkInterface card)
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(AdapterClassKey);
        if (classKey is null) return default;
        foreach (var name in classKey.GetSubKeyNames())
        {
            using var key = classKey.OpenSubKey(name);
            if (key?.GetValue("NetCfgInstanceId") is not string id || !id.Equals(card.Id, StringComparison.OrdinalIgnoreCase)) continue;
            return new SettingsRead(true, KeywordsLevel(key, WakeKeywords.MagicNames, WakeKeywords.MagicDescription), KeywordsLevel(key, WakeKeywords.PatternNames, WakeKeywords.PatternDescription));
        }
        return default;
    }

    // 2 when any matching keyword is on, 1 when one is off, 0 when none is present.
    private static int KeywordsLevel(RegistryKey driverKey, string[] names, string descriptionPattern)
    {
        var level = 0;
        var matched = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        using (var parameters = driverKey.OpenSubKey(@"Ndi\Params"))
        {
            if (parameters is not null)
                foreach (var keyword in parameters.GetSubKeyNames())
                {
                    using var one = parameters.OpenSubKey(keyword);
                    if (one?.GetValue("ParamDesc") is string desc && Regex.IsMatch(desc, descriptionPattern, RegexOptions.IgnoreCase)) matched.Add(keyword);
                }
        }
        foreach (var keyword in matched)
        {
            var value = KeywordLevel(driverKey.GetValue(keyword));
            if (value == 2) return 2;
            if (value == 1) level = 1;
        }
        return level;
    }

    private static int KeywordLevel(object? value) => value is null ? 0 : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() switch { "1" => 2, "0" => 1, _ => 0 };

    // Windows' permission for the card to wake the PC (root\wmi, keyed by the card's device id). True when the card has a row there.
    private static PermissionRead ReadPermission(NetworkInterface card)
    {
        var enabled = false;
        var device = PnpId(card.Id);
        if (device.Length == 0) return default;
        var found = false;
        using var wake = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Enable FROM MSPower_DeviceWakeEnable");
        foreach (var row in wake.Get())
            if (row["InstanceName"] is string name && name.StartsWith(device, StringComparison.OrdinalIgnoreCase))
            {
                enabled = row["Enable"] is true;
                found = true;
            }
        return new PermissionRead(found, enabled);
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
