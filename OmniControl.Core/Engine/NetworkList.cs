using System.Net.NetworkInformation;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Core.Engine;

/// How Windows trusts a network (Settings > Network > the network's "Network profile type").
public enum NetworkCategory
{
    Public = 0,
    Private = 1,
    Domain = 2,
}

/// What a saved network is reached over (Windows' profile NameType), so it is offered under the adapters of that kind.
public enum NetworkMedium { Other, Wired, Wireless, Vpn, Mobile }

/// One network adapter: its id (<c>NetworkInterface.Id</c>, "{GUID}" in capitals), name ("Wi-Fi"), kind and whether it is connected.
public sealed record NetAdapter(string Id, string Name, AdapterKind Kind, bool IsUp);

/// One network Windows has saved: its id (the profile's GUID), name, how Windows trusts it, what it is reached over, whether it is metered
/// (known only while connected) and the adapters it is connected through now (none: not connected).
public sealed record SavedNetwork(string Id, string Name, NetworkCategory Category, NetworkMedium Medium, bool Metered, IReadOnlyList<string> Adapters)
{
    public bool Connected => Adapters.Count > 0;
}

/// The PC's network adapters and saved networks at one moment (<see cref="NetworkList.Read"/>).
public sealed record NetworkSnapshot(IReadOnlyList<NetAdapter> Adapters, IReadOnlyList<SavedNetwork> Networks)
{
    public static readonly NetworkSnapshot Empty = new(Array.Empty<NetAdapter>(), Array.Empty<SavedNetwork>());

    /// The network an adapter is connected to now, or null.
    public SavedNetwork? On(string adapterId) => Networks.FirstOrDefault(n => n.Adapters.Contains(adapterId, StringComparer.OrdinalIgnoreCase));

    /// Each connected adapter's category by its GUID; an adapter on two networks counts as its least trusted one.
    public IReadOnlyDictionary<Guid, NetworkCategory> Categories
    {
        get
        {
            var found = new Dictionary<Guid, NetworkCategory>();
            foreach (var network in Networks)
                foreach (var adapter in network.Adapters)
                    if (Guid.TryParse(adapter, out var id))
                        found[id] = found.TryGetValue(id, out var other) && other < network.Category ? other : network.Category;
            return found;
        }
    }

    /// True when both say the same (a refresh that found nothing new tells no one).
    public bool Same(NetworkSnapshot other) => Key == other.Key;

    private string Key => string.Join("|", Adapters) + "#" + string.Join("|", Networks.Select(n =>
        $"{n.Id},{n.Name},{n.Category},{n.Medium},{n.Metered},{string.Join(",", n.Adapters)}"));
}

/// THE reader of the PC's networks: the adapters (<see cref="NetworkInterface"/>, without NDIS filter instances) and every network Windows
/// has saved, from Windows' Network List Manager (name, category, the adapters it is on, the connection's cost), with its medium from the
/// profile list. COM and the registry: call off the UI thread. Throws <see cref="COMException"/> when Windows' list can't be read.
public static class NetworkList
{
    private const int AllNetworks = 3; // NLM_ENUM_NETWORK_ALL: connected and not
    private const uint CostFixed = 0x2, CostVariable = 0x4; // NLM_CONNECTION_COST: a metered connection
    private const string Profiles = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\";

    public static NetworkSnapshot Read()
    {
        var rows = AdapterTable.Read();
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) && !rows.GetValueOrDefault(n.Id).Filter)
            .Select(n => new NetAdapter(n.Id.ToUpperInvariant(), n.Name,
                AdapterSpeed.KindOf(n.NetworkInterfaceType, n.Description, !rows.TryGetValue(n.Id, out var row) || row.Hardware), n.OperationalStatus == OperationalStatus.Up))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new NetworkSnapshot(adapters, Networks());
    }

    private static List<SavedNetwork> Networks()
    {
        INetworkListManager? manager = null;
        IEnumNetworks? networks = null;
        try
        {
            manager = (INetworkListManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClsid, throwOnError: true)!)!;
            networks = manager.GetNetworks(AllNetworks);
            var found = new List<SavedNetwork>();
            while (true)
            {
                networks.Next(1, out var network, out var fetched);
                if (fetched == 0 || network is null) break;
                try
                {
                    var id = network.GetNetworkId();
                    var (adapters, metered) = network.IsConnected() ? Connections(network) : (new List<string>(), false);
                    found.Add(new SavedNetwork(Text(id), network.GetName(), (NetworkCategory)network.GetCategory(), MediumOf(id), metered, adapters));
                }
                finally { Marshal.ReleaseComObject(network); }
            }
            return found.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            if (networks is not null) Marshal.ReleaseComObject(networks);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }

    /// The adapters a connected network is on, and whether any of its connections is metered.
    private static (List<string> Adapters, bool Metered) Connections(INetwork network)
    {
        var adapters = new List<string>();
        var metered = false;
        var connections = network.GetNetworkConnections();
        try
        {
            while (true)
            {
                connections.Next(1, out var connection, out var fetched);
                if (fetched == 0 || connection is null) break;
                try
                {
                    adapters.Add(Text(connection.GetAdapterId()));
                    if (connection is INetworkConnectionCost cost)
                    {
                        cost.GetCost(out var flags);
                        metered |= (flags & (CostFixed | CostVariable)) != 0;
                    }
                }
                finally { Marshal.ReleaseComObject(connection); }
            }
        }
        finally { Marshal.ReleaseComObject(connections); }
        return (adapters, metered);
    }

    private static string Text(Guid id) => "{" + id.ToString().ToUpperInvariant() + "}";

    /// The profile's NameType: 6 wired, 71 wireless, 23 VPN, 243 mobile broadband.
    private static NetworkMedium MediumOf(Guid id)
    {
        using var profile = Registry.LocalMachine.OpenSubKey(Profiles + Text(id));
        return profile?.GetValue("NameType") switch
        {
            6 => NetworkMedium.Wired,
            71 => NetworkMedium.Wireless,
            23 => NetworkMedium.Vpn,
            243 => NetworkMedium.Mobile,
            _ => NetworkMedium.Other,
        };
    }

    internal static readonly Guid ManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    // Interfaces from netlistmgr.h, methods in vtable order (only the ones used are named for what they do).
    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        IEnumNetworks GetNetworks(int flags);
    }

    [ComImport, Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworks
    {
        void NewEnum();
        void Next(uint count, out INetwork? network, out uint fetched);
    }

    [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        void NewEnum();
        void Next(uint count, out INetworkConnection? connection, out uint fetched);
    }

    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        void GetNetwork();
        void IsConnectedToInternet();
        void IsConnected();
        void GetConnectivity();
        void GetConnectionId();
        Guid GetAdapterId();
    }

    [ComImport, Guid("DCB0000A-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INetworkConnectionCost
    {
        void GetCost(out uint cost);
    }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetName();
        void GetDescription();
        void SetDescription();
        Guid GetNetworkId();
        void GetDomainType();
        IEnumNetworkConnections GetNetworkConnections();
        void GetTimeCreatedAndConnected();
        void IsConnectedToInternet();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsConnected();
        void GetConnectivity();
        int GetCategory();
    }
}
