using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// The page's two entries in the master list of hardware watches (<see cref="HardwareWatches"/>): the monitors and the list of displays.
public sealed partial class DisplaysService
{
    /// The monitors the hardware controls talk to (DDC/CI): opened again whole, because after Remote Desktop, sleep or a driver reset the old handles can be dead while the displays look the same.
    public const string HardwareWatchId = "displays.hardware";

    /// The displays Windows lists (the software controls): looked at again, and only redone when the list differs.
    public const string SoftwareWatchId = "displays.software";

    private const HardwareSignal DisplaySignals = HardwareSignal.Display | HardwareSignal.Session | HardwareSignal.Resume | HardwareSignal.DisplayPower;

    private readonly MonitorExpectation _expected = new();

    private void RegisterWatches(IHardwareWatches hardware)
    {
        // The monitors' refresh is the Refresh button's own work (ReloadNow), so a signal does exactly what pressing Refresh does.
        hardware.Register(new HardwareWatch(HardwareWatchId, "Monitors", DisplaySignals, ReloadNow,
            LooksStale: () => MonitorsLookWrong(strict: false), LooksUnsettled: () => MonitorsLookWrong(strict: true), Describe: DescribeMonitors));
        hardware.Register(new HardwareWatch(SoftwareWatchId, "Displays", DisplaySignals, RefreshDisplays, Describe: DescribeDisplays));
    }

    /// The monitors' check (timer thread; cheap: Windows' display list, and each monitor's answer, which is kept once read). Stale: the
    /// displays Windows lists differ from the ones the monitors were opened for; fewer monitors than these displays had before; or a monitor
    /// answering fewer codes than it did. <paramref name="strict"/> (the tries after an announcement and at start) also counts a desktop
    /// display without a monitor handle and a monitor that does not answer at all, which may only be waking.
    private bool MonitorsLookWrong(bool strict)
    {
        var monitors = _engine.Monitors;
        var external = _engine.Displays.Where(d => !d.IsInternal).ToList();
        var stale = _engine.DisplaysChanged();
        stale |= _expected.LooksStale(external.Select(d => d.DevicePath), monitors.Count);
        var desktop = monitors.Where(m => !m.Info.IsInternal).ToList();
        foreach (var monitor in desktop)
        {
            var codes = monitor.Probe().Features.Count;
            stale |= _expected.AnswersLess(KeyOf(monitor), codes); // every monitor is asked, so each one's best is learned
            stale |= strict && codes == 0;
        }
        return stale || (strict && desktop.Count < external.Count);
    }

    /// The monitors for the log: "3 displays, 2 DDC/CI monitors: PHL 27M1F5800 19 codes, DELL U2720 no answer".
    private string DescribeMonitors()
    {
        var monitors = _engine.Monitors;
        var displays = _engine.Displays.Count;
        var each = monitors.Select(m => $"{m.Info.Name} {(m.Info.IsInternal ? "built-in" : m.Probe() is { Answers: true } c ? $"{c.Features.Count} codes" : "no answer")}");
        return $"{displays} display{(displays == 1 ? "" : "s")}, {monitors.Count} DDC/CI monitor{(monitors.Count == 1 ? "" : "s")}: {string.Join(", ", each)}";
    }

    /// The displays for the log: "3 displays: PHL 27M1F5800, DELL U2720, Built-in display".
    private string DescribeDisplays()
    {
        var displays = _engine.Displays;
        return $"{displays.Count} display{(displays.Count == 1 ? "" : "s")}: {string.Join(", ", displays.Select(d => d.Name))}";
    }

    /// A monitor's lasting name for what it answered: its device path (the display's name when Windows gave none).
    private static string KeyOf(IMonitor monitor) => string.IsNullOrEmpty(monitor.Info.DevicePath) ? monitor.Info.DeviceName : monitor.Info.DevicePath;

    /// Asks every monitor what it supports (worker thread; each in parallel, each answer kept on the monitor so the models' own read is free),
    /// so the watch's check after a refresh sees what the monitors really answered and not only that they were opened.
    private void ProbeMonitors() =>
        Parallel.ForEach(_engine.Monitors, monitor => Guard.Run($"Displays: read {monitor.Info.Name}", () => monitor.Probe()));
}
