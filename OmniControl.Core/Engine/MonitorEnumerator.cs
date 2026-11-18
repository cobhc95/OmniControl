using OmniControl.Core.Interfaces;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// Opens the DDC/CI monitors: every display that has a physical monitor handle (a laptop's own panel has none). Windows is asked first how
/// many physical monitors a display has: a display that mirrors two would refuse a request for one, and the whole display would be lost.
/// The first is kept (the display is one entry); the others are released at once.
/// </summary>
public sealed class MonitorEnumerator : IMonitorEnumerator
{
    public List<IMonitor> Enumerate()
    {
        var monitors = new List<IMonitor>();
        foreach (var (handle, info) in DisplayEnumerator.Enumerate())
        {
            // A display whose count Windows will not give is still asked for one handle, as before the count was asked.
            if (!Dxva2Native.GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count) || count == 0) count = 1;
            var physical = new Dxva2Native.PHYSICAL_MONITOR[count];
            if (!Dxva2Native.GetPhysicalMonitorsFromHMONITOR(handle, count, physical)) continue;
            if (count > 1) Dxva2Native.DestroyPhysicalMonitors(count - 1, physical[1..]);
            if (physical[0].hPhysicalMonitor == IntPtr.Zero) continue;
            monitors.Add(new DdcCiMonitor(info, physical[0]));
        }
        return monitors;
    }
}
