using System.Management;

namespace OmniControl.Core.Engine;

/// <summary>
/// Raises <see cref="Changed"/> (connected, device name; on a worker thread) when a USB device is plugged in or removed.
/// Windows announces "something changed" without a name, so each announcement triggers one look at the USB device list,
/// compared with the previous list. One plug makes several entries (the device, its interfaces), so the look waits for them to settle.
/// </summary>
public sealed class UsbDeviceWatcher : IDisposable
{
    private const string DeviceListQuery = "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'";
    private const string ChangeQuery = "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3"; // 2 = arrival, 3 = removal

    // Windows' own names for the parts of a device; a more specific name (the product) is shown when there is one.
    private static readonly string[] GenericNames = { "USB Composite Device", "USB Input Device", "USB Root Hub", "Generic USB Hub", "USB Hub", "Unknown" };

    private readonly TimeSpan _settle;
    private readonly object _gate = new();
    private ManagementEventWatcher? _watcher;
    private Timer? _timer;
    private Dictionary<string, string> _known = new();

    public event Action<bool, string>? Changed;

    /// <param name="settle">How long after the last announcement to wait before looking at the device list.</param>
    public UsbDeviceWatcher(TimeSpan settle) => _settle = settle;

    public void Start()
    {
        _timer = new Timer(_ => Scan());
        Task.Run(() => { lock (_gate) _known = ReadDevices(); }); // WMI is slow to answer the first time; keep it off the caller's thread
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\cimv2"), new WqlEventQuery(ChangeQuery));
            _watcher.EventArrived += (_, _) => _timer.Change(_settle, Timeout.InfiniteTimeSpan);
            _watcher.Start();
        }
        catch (ManagementException)
        {
            _watcher = null; // WMI device events unavailable: there is simply no USB pop-up
        }
    }

    /// <summary>A look at the device list (or a subscriber of <see cref="Changed"/>) threw on the timer thread; the watcher keeps going.</summary>
    public event Action<Exception>? Failed;

    private void Scan()
    {
        try
        {
            Look();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed?.Invoke(ex); // a timer thread must not end the process
        }
    }

    private void Look()
    {
        var now = ReadDevices();
        List<string> added, removed;
        lock (_gate)
        {
            added = now.Where(d => !_known.ContainsKey(d.Key)).Select(d => d.Value).ToList();
            removed = _known.Where(d => !now.ContainsKey(d.Key)).Select(d => d.Value).ToList();
            _known = now;
        }
        if (added.Count > 0) Changed?.Invoke(true, BestName(added));
        if (removed.Count > 0) Changed?.Invoke(false, BestName(removed));
    }

    private static string BestName(List<string> names) =>
        names.FirstOrDefault(n => !GenericNames.Any(g => n.StartsWith(g, StringComparison.OrdinalIgnoreCase))) ?? names[0];

    private static Dictionary<string, string> ReadDevices()
    {
        var devices = new Dictionary<string, string>();
        try
        {
            using var search = new ManagementObjectSearcher(DeviceListQuery);
            foreach (var item in search.Get())
            {
                using (item)
                    if (item["PNPDeviceID"] is string id && item["Name"] is string name) devices[id] = name;
            }
        }
        catch (ManagementException)
        {
            // WMI busy or unavailable: report no change this time.
        }
        return devices;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch (ManagementException) { /* the watcher may already be gone at shutdown */ }
    }
}
