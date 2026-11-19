using System.Management;

namespace OmniControl.Core.Engine;

/// Raises <see cref="Changed"/> (connected, device name; on the caller's thread) when the USB device list differs from the last look.
/// Windows announces "something changed" without a name and the app hears it once (its hardware watches, <see cref="HardwareSignal.Device"/>),
/// so the owner calls <see cref="Look"/> once a burst of announcements has settled (one plug makes several entries: the device, its
/// interfaces), and the look compares the device list with the previous one. Nothing listens or polls here.
public sealed class UsbDeviceWatcher
{
    private const string DeviceListQuery = "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'";

    // Windows' own names for the parts of a device; a more specific name (the product) is shown when there is one.
    private static readonly string[] GenericNames = { "USB Composite Device", "USB Input Device", "USB Root Hub", "Generic USB Hub", "USB Hub", "Unknown" };

    private readonly object _gate = new();
    private Dictionary<string, string>? _known; // null while stopped (or until the first list is read)

    public event Action<bool, string>? Changed;

    /// Takes the device list as it is now (on a worker: WMI is slow to answer the first time); looks compare with it from then on.
    public void Start() => Task.Run(() =>
    {
        if (ReadDevices() is { } now) lock (_gate) _known ??= now;
    });

    /// Stops comparing until the next <see cref="Start"/>.
    public void Stop()
    {
        lock (_gate) _known = null;
    }

    /// Reads the device list and raises <see cref="Changed"/> for what came or went since the last look (worker thread; nothing while stopped).
    public void Look()
    {
        lock (_gate) if (_known is null) return;
        if (ReadDevices() is not { } now) return;
        List<string> added, removed;
        lock (_gate)
        {
            if (_known is not { } known) return;
            added = now.Where(d => !known.ContainsKey(d.Key)).Select(d => d.Value).ToList();
            removed = known.Where(d => !now.ContainsKey(d.Key)).Select(d => d.Value).ToList();
            _known = now;
        }
        if (added.Count > 0) Changed?.Invoke(true, BestName(added));
        if (removed.Count > 0) Changed?.Invoke(false, BestName(removed));
    }

    private static string BestName(List<string> names) =>
        names.FirstOrDefault(n => !GenericNames.Any(g => n.StartsWith(g, StringComparison.OrdinalIgnoreCase))) ?? names[0];

    /// The USB devices now, or null when WMI did not answer (a busy WMI must not look like every device leaving).
    private static Dictionary<string, string>? ReadDevices()
    {
        var devices = new Dictionary<string, string>();
        try
        {
            using var search = new ManagementObjectSearcher(DeviceListQuery);
            using var found = search.Get();
            foreach (var item in found)
            {
                using (item)
                    if (item["PNPDeviceID"] is string id && item["Name"] is string name) devices[id] = name;
            }
        }
        catch (ManagementException)
        {
            return null; // WMI busy or unavailable: no change this time
        }
        return devices;
    }
}
