using OmniControl.Core.Interfaces;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>Opens the DDC/CI monitors: every display that has a physical monitor handle (a laptop's own panel has none).</summary>
public sealed class MonitorEnumerator : IMonitorEnumerator
{
    public List<IMonitor> Enumerate()
    {
        var monitors = new List<IMonitor>();
        foreach (var (handle, info) in DisplayEnumerator.Enumerate())
        {
            var physical = new Dxva2Native.PHYSICAL_MONITOR[1];
            if (!Dxva2Native.GetPhysicalMonitorsFromHMONITOR(handle, 1, physical) || physical[0].hPhysicalMonitor == IntPtr.Zero) continue;
            monitors.Add(new DdcCiMonitor(info, physical[0]));
        }
        return monitors;
    }
}
