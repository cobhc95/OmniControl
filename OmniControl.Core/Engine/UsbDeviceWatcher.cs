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
    public event Action<IReadOnlyDictionary<string, string>>? Seen;

    private void Read(Dictionary<string, string> baseline)
    {
        if (ReadDevices() is not { } now) return;
        lock (_gate)
        {
            if (!ReferenceEquals(_known, baseline)) return;
            var first = _starting;
            _starting = false;
            _known = now;
            Seen?.Invoke(now);
            if (first) return;
            Raise(true, now, baseline);
            Raise(false, baseline, now);
        }
    }

    private bool _starting;

    /// Takes the baseline on a worker because the first WMI read can be slow.
    public void Start()
    {
        lock (_gate)
        {
            if (_known is not null) return;
            _known = new(StringComparer.OrdinalIgnoreCase);
            _starting = true;
        }
        _ = Task.Run(Look);
    }

    /// Stops comparing until the next <see cref="Start"/>.
    public void Stop()
    {
        lock (_gate) { _known = null; _starting = false; }
    }

    /// Reads the device list and raises <see cref="Changed"/> for what came or went since the last look (worker thread; nothing while stopped).
    public void Look()
    {
        Dictionary<string, string>? baseline;
        lock (_gate) baseline = _known;
        if (baseline is not null) Read(baseline);
    }

    private void Raise(bool connected, Dictionary<string, string> earlier, Dictionary<string, string> other)
    {
        var changed = earlier.Where(d => !other.ContainsKey(d.Key)).Select(d => d.Value).ToList();
        if (changed.Count > 0) Changed?.Invoke(connected, BestName(changed));
    }

    private static string BestName(List<string> names) =>
        names.FirstOrDefault(n => !GenericNames.Any(g => n.StartsWith(g, StringComparison.OrdinalIgnoreCase))) ?? names[0];

    /// The USB devices now, or null when WMI did not answer (a busy WMI must not look like every device leaving).
    private static Dictionary<string, string>? ReadDevices()
    {
        var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
