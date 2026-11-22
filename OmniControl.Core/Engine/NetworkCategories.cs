using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// How Windows trusts a network (Settings > Network > the network's "Network profile type").
public enum NetworkCategory
{
    Public = 0,
    Private = 1,
    Domain = 2,
}

/// Each connected adapter's network category, from Windows' Network List Manager. COM: call off the UI thread.
public static class NetworkCategories
{
    /// The category by adapter id (the GUID of <c>NetworkInterface.Id</c>). Throws <see cref="COMException"/> when Windows' list can't be read.
    public static IReadOnlyDictionary<Guid, NetworkCategory> Read()
    {
        INetworkListManager? manager = null;
        IEnumNetworkConnections? connections = null;
        try
        {
            manager = (INetworkListManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClsid, throwOnError: true)!)!;
            connections = manager.GetNetworkConnections();
            var found = new Dictionary<Guid, NetworkCategory>();
            while (true)
            {
                connections.Next(1, out var connection, out var fetched);
                if (fetched == 0 || connection is null) break;
                INetwork? network = null;
                try
                {
                    network = connection.GetNetwork();
                    // An adapter on two networks counts as its least trusted one.
                    var category = (NetworkCategory)network.GetCategory();
                    var adapter = connection.GetAdapterId();
                    found[adapter] = found.TryGetValue(adapter, out var other) && other < category ? other : category;
                }
                finally
                {
                    if (network is not null) Marshal.ReleaseComObject(network);
                    Marshal.ReleaseComObject(connection);
                }
            }
            return found;
        }
        finally
        {
            if (connections is not null) Marshal.ReleaseComObject(connections);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }

    private static readonly Guid ManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    // Dual interfaces from netlistmgr.h, methods in vtable order (only the ones used are named for what they do).
    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        void GetNetworks();
        void GetNetwork();
        IEnumNetworkConnections GetNetworkConnections();
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
        INetwork GetNetwork();
        void IsConnectedToInternet();
        void IsConnected();
        void GetConnectivity();
        void GetConnectionId();
        Guid GetAdapterId();
    }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        void GetName();
        void SetName();
        void GetDescription();
        void SetDescription();
        void GetNetworkId();
        void GetDomainType();
        void GetNetworkConnections();
        void GetTimeCreatedAndConnected();
        void IsConnectedToInternet();
        void IsConnected();
        void GetConnectivity();
        int GetCategory();
    }
}
