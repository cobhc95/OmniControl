using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Displays;

/// <summary>The page's two entries in the master list of hardware watches (<see cref="HardwareWatches"/>): the monitors and the list of displays.</summary>
public sealed partial class DisplaysPageViewModel
{
    /// <summary>The monitors the hardware controls talk to (DDC/CI): opened again whole, because after Remote Desktop, sleep or a driver reset the old handles can be dead while the displays look the same.</summary>
    public const string HardwareWatchId = "displays.hardware";

    /// <summary>The displays Windows lists (the software controls): looked at again, and only redone when the list differs.</summary>
    public const string SoftwareWatchId = "displays.software";

    private const HardwareSignal DisplaySignals = HardwareSignal.Display | HardwareSignal.Session | HardwareSignal.Resume;

    private readonly MonitorExpectation _expected = new();

    private void RegisterWatches(HardwareWatches hardware)
    {
        // The monitors' refresh is the Refresh button's own work (ReloadNow), so a signal does exactly what pressing Refresh does.
        hardware.Register(new HardwareWatch(HardwareWatchId, "Monitors", DisplaySignals, ReloadNow, LooksStale: MonitorsLookStale));
        hardware.Register(new HardwareWatch(SoftwareWatchId, "Displays", DisplaySignals, RescanNow));
    }

    /// <summary>Fewer DDC/CI monitors than these displays had before, or none while Windows lists a desktop monitor: what "undetected after Remote Desktop" looks like.</summary>
    private bool MonitorsLookStale()
    {
        var displays = _engine.Displays;
        var monitors = _engine.Monitors.Count;
        return _expected.LooksStale(displays.Where(d => !d.IsInternal).Select(d => d.DevicePath), monitors);
    }
}
