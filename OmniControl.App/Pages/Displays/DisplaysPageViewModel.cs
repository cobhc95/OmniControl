using System.ComponentModel;
using System.Management;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Display;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The Displays page: a Hardware group (what each monitor itself can do, over DDC/CI, and a laptop's panel) and a Software group (what
/// OmniControl does to the picture on any display), each holding one collapsed group per display. The displays are looked at again,
/// cheaply, whenever the window is shown or hidden and when Windows reports a display change; the groups of the
/// displays that come and go follow. The page's hotkey actions are one per control, not per display (<c>DisplaysPageViewModel.Hotkeys.cs</c>).
/// </summary>
public sealed partial class DisplaysPageViewModel : PageViewModel, IDisposable
{
    private const string LaptopId = "laptop";

    /// <summary>What a worker thread found out about the displays, to be shown on the UI thread.</summary>
    private sealed record Snapshot(IReadOnlyList<DisplayInfo> Displays, IReadOnlyList<IMonitor> Monitors, bool HasLaptop);

    private readonly MonitorEngine _engine;
    private readonly HotkeyRegistry _hotkeys;
    private readonly OsdService _osd;
    private readonly SoftwareDisplayService _software;
    private readonly Action<string> _report;
    private readonly LevelStepper _levels = new();
    private readonly ThrottledRun _rescan;
    private readonly SoftwareGlobalItem _global;
    private readonly List<HardwareDisplayItem> _hardware = new();
    private readonly List<SoftwareDisplayItem> _softwareItems = new();
    private IReadOnlyList<IMonitor> _shownMonitors = Array.Empty<IMonitor>();
    private bool _hasLaptop;

    public override string Title => "Displays";
    public override string Icon => "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M4,6 V16 H20 V6 Z";
    public override string Keywords => "monitor display screen brightness contrast ddc laptop panel gamma digital saturation temperature warm cool input source colour color";

    /// <summary>What the monitors themselves can do (DDC/CI and the laptop panel): one inner group per display.</summary>
    public ParentGroup Hardware { get; } = new("Displays/Hardware", DisplayControls.HardwareGroup, NoMonitors) { SummaryBesideName = true, ExpandedByDefault = true };

    /// <summary>What OmniControl does to the picture itself: the whole-desktop controls, then one inner group per display.</summary>
    public ParentGroup Software { get; }

    public override IEnumerable<SearchEntry> SearchEntries =>
        _hardware.SelectMany(h => h.Cards.Select(c => Entry(c, $"{c.Label} ({h.Name})", $"monitor display screen ddc {h.Name}", DisplayControls.HardwareGroup)))
            .Concat(_global.Cards.Select(c => Entry(c, c.Label, "monitor display screen digital software", DisplayControls.SoftwareGroup)))
            .Concat(_softwareItems.SelectMany(s => s.Cards.Select(c =>
                Entry(c, $"{c.Label} ({s.Name})", $"monitor display screen digital software {s.Name}", DisplayControls.SoftwareGroup))));

    private static string NoMonitors => "No monitors available";

    public DisplaysPageViewModel(MonitorEngine engine, HotkeyRegistry hotkeys, OsdService osd, SoftwareDisplayService software, Action<string> report)
    {
        (_engine, _hotkeys, _osd, _software, _report) = (engine, hotkeys, osd, software, report);
        _rescan = new ThrottledRun(Timing.DisplayRescanGap, RescanNow);

        foreach (var spec in DisplayControls.All) osd.Declare(spec.Osd, icon: spec.Icon);

        _global = new SoftwareGlobalItem(software, osd, Title, report, ResetAllSoftware);
        Software = new ParentGroup("Displays/Software", DisplayControls.SoftwareGroup, string.Empty, _global.Cards.Cast<object>().ToList()) { ExpandedByDefault = true };
        UpdateSoftwareSummary();
        RegisterActions();
        Task.Run(RefreshNow);
    }

    // ---------------------------------------------------------------- looking at the displays again

    public override void OnShown() => _rescan.Request();

    public override void OnWindowShownChanged(bool shown) => _rescan.Request();

    public override void OnDisplaysChanged() => _rescan.Request(Timing.DisplayChangeSettle);

    /// <summary>Opens every monitor again and asks each what it supports (the Refresh button, F5).</summary>
    [RelayCommand]
    private void Reload() => Task.Run(() =>
    {
        _engine.RefreshMonitors();
        RefreshNow();
    });

    /// <summary>The throttled check (worker thread): only when the attached displays differ from the last look is anything redone.</summary>
    private void RescanNow()
    {
        try
        {
            if (_engine.Rescan()) RefreshNow();
        }
        catch (Win32Exception)
        {
            // Windows could not list the displays just now (one was being switched); the next event looks again.
        }
    }

    /// <summary>Shows the engine's current displays (worker thread), whatever Windows says.</summary>
    private void RefreshNow()
    {
        try { Publish(); }
        catch (Exception ex) when (ex is Win32Exception or ManagementException)
        {
            // WMI could not answer for the laptop panel: show the monitors without it.
            var snapshot = new Snapshot(_engine.Displays, _engine.Monitors, false);
            Dispatcher.UIThread.Post(() => Show(snapshot));
        }
    }

    /// <summary>Collects what the page needs from the engine (worker thread: the laptop panel is asked through WMI) and shows it.</summary>
    private void Publish()
    {
        var laptop = _engine.Laptop;
        var hasLaptop = laptop.IsAvailable && laptop.GetBrightness(out _, out var max).Success && max > 0;
        var snapshot = new Snapshot(_engine.Displays, _engine.Monitors, hasLaptop);
        Dispatcher.UIThread.Post(() => Show(snapshot));
    }

    // ---------------------------------------------------------------- showing the displays

    private void Show(Snapshot snapshot)
    {
        _hasLaptop = snapshot.HasLaptop;
        ShowHardware(snapshot);
        ShowSoftware(snapshot.Displays);
        RefreshActionDevices();
        Hardware.Summary = _hardware.Count switch
        {
            0 => NoMonitors,
            1 => "1 monitor available",
            _ => $"{_hardware.Count} monitors available",
        };
    }

    /// <summary>A monitor that is also the laptop's panel is shown once, as the laptop panel (its brightness is Windows').</summary>
    private void ShowHardware(Snapshot snapshot)
    {
        var monitors = snapshot.Monitors.Where(m => !(snapshot.HasLaptop && m.Info.IsInternal)).ToList();
        if (monitors.SequenceEqual(_shownMonitors) && _hardware.Any(h => h.IsLaptopPanel) == snapshot.HasLaptop) return; // nothing changed

        foreach (var old in _hardware)
        {
            old.Dispose();
        }
        _hardware.Clear();
        Hardware.Subgroups.Clear();
        _shownMonitors = monitors;

        if (snapshot.HasLaptop) Add(new HardwareDisplayItem(LaptopId, "Built-in display", new LaptopAccess(_engine.Laptop), _levels, _osd, _report));
        foreach (var monitor in monitors)
            Add(new HardwareDisplayItem(monitor.Info.DeviceName, monitor.Info.Name, new MonitorAccess(monitor), _levels, _osd, _report));
    }

    private void Add(HardwareDisplayItem display)
    {
        _hardware.Add(display);
        Hardware.Subgroups.Add(display.Group);
        display.Start();
    }

    /// <summary>Software groups follow the attached displays: new ones appear, gone ones are forgotten, the others keep their values.</summary>
    private void ShowSoftware(IReadOnlyList<DisplayInfo> displays)
    {
        foreach (var gone in _softwareItems.Where(s => displays.All(d => d.DeviceName != s.Id)).ToList())
        {
            gone.Forget();
            _softwareItems.Remove(gone);
        }

        var fresh = new List<SoftwareDisplayItem>();
        foreach (var display in displays)
        {
            var item = _softwareItems.FirstOrDefault(s => s.Id == display.DeviceName);
            item ??= new SoftwareDisplayItem(display, _software, _report);
            fresh.Add(item);
        }

        if (!fresh.SequenceEqual(_softwareItems))
        {
            _softwareItems.Clear();
            _softwareItems.AddRange(fresh);
            Software.Subgroups.Clear();
            foreach (var item in fresh) Software.Subgroups.Add(item.Group);
        }
        UpdateSoftwareSummary();
    }

    private void UpdateSoftwareSummary()
    {
        var count = _global.Cards.Count + _softwareItems.Sum(s => s.Cards.Count);
        Software.Summary = count == 1 ? "1 setting" : $"{count} settings";
    }

    /// <summary>Every display's software picture and the saturation back to normal.</summary>
    private void ResetAllSoftware()
    {
        foreach (var item in _softwareItems) item.Reset();
        _global.Reset();
        _software.ResetAll();
    }

    public void Dispose()
    {
        _rescan.Dispose();
        foreach (var display in _hardware) display.Dispose();
    }
}
