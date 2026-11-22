using System.Runtime.InteropServices.ComTypes;

namespace OmniControl.Core.Engine;

/// Windows' Network List Manager announcements, heard only between <see cref="Start"/> and <see cref="Dispose"/>: a network saved,
/// forgotten, connected, disconnected or changed (name, category), and a connection's cost changed (metered). Each is told as one short
/// text on Windows' own thread; the listener reads nothing itself (<see cref="NetworkList.Read"/> does). Start and Dispose: an MTA thread.
public sealed class NetworkListEvents : IDisposable
{
    private static readonly Guid NetworkEventsId = typeof(INetworkEvents).GUID, CostEventsId = typeof(INetworkCostManagerEvents).GUID;

    private readonly object _manager;
    private readonly Action<string> _failed;
    private readonly List<(IConnectionPoint Point, int Cookie)> _advised = new();

    private NetworkListEvents(object manager, Action<string> failed) => (_manager, _failed) = (manager, failed);

    /// Listens; throws <see cref="COMException"/> when Windows refuses (nothing is left listening then).
    public static NetworkListEvents Start(Action<string> told, Action<string> failed)
    {
        var events = new NetworkListEvents(Activator.CreateInstance(Type.GetTypeFromCLSID(NetworkList.ManagerClsid, throwOnError: true)!)!, failed);
        try
        {
            var sink = new Sink(told, failed);
            foreach (var id in new[] { NetworkEventsId, CostEventsId })
            {
                var guid = id;
                ((IConnectionPointContainer)events._manager).FindConnectionPoint(ref guid, out var found);
                var point = found ?? throw new COMException("Windows' network list has no such announcement.");
                point.Advise(sink, out var cookie);
                events._advised.Add((point, cookie));
            }
            return events;
        }
        catch (COMException)
        {
            events.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var (point, cookie) in _advised)
        {
            try { point.Unadvise(cookie); }
            catch (COMException ex) { _failed($"network list listener: Windows' list went away before it could be stopped ({ex.Message})."); }
            Marshal.ReleaseComObject(point);
        }
        _advised.Clear();
        Marshal.ReleaseComObject(_manager);
    }

    /// INetworkEvents. Public only because Windows can only call interfaces that are visible to COM.
    [ComVisible(true), Guid("DCB00004-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INetworkEvents
    {
        [PreserveSig] int NetworkAdded(Guid network);
        [PreserveSig] int NetworkDeleted(Guid network);
        [PreserveSig] int NetworkConnectivityChanged(Guid network, int connectivity);
        [PreserveSig] int NetworkPropertyChanged(Guid network, int change);
    }

    /// INetworkCostManagerEvents.
    [ComVisible(true), Guid("DCB00009-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INetworkCostManagerEvents
    {
        [PreserveSig] int CostChanged(uint cost, IntPtr destination);
        [PreserveSig] int DataPlanStatusChanged(IntPtr destination);
    }

    private sealed class Sink(Action<string> told, Action<string> failed) : INetworkEvents, INetworkCostManagerEvents
    {
        public int NetworkAdded(Guid network) => Tell("network saved");
        public int NetworkDeleted(Guid network) => Tell("network forgotten");
        public int NetworkConnectivityChanged(Guid network, int connectivity) => Tell("connectivity changed");
        public int NetworkPropertyChanged(Guid network, int change) => Tell("network changed");
        public int CostChanged(uint cost, IntPtr destination) => Tell("cost changed");
        public int DataPlanStatusChanged(IntPtr destination) => 0;

        // An exception must never go back into Windows' thread: it is reported and the listener goes on.
        private int Tell(string what)
        {
            try { told(what); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failed($"network list listener failed: {ex}"); }
            return 0;
        }
    }
}
