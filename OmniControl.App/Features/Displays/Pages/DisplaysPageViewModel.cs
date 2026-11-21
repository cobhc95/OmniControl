using System.ComponentModel;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Shared.Presets;
using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// The Displays page: a Hardware group (what each monitor itself can do, over DDC/CI, and a laptop's panel) and a Software group (what
/// OmniControl does to the picture on any display), each holding one collapsed group per display. The displays are looked at again,
/// cheaply, whenever the window is shown or hidden and when Windows reports a display change; the groups of the
/// displays that come and go follow. The page's hotkey actions are one per control, not per display (<c>DisplaysPageViewModel.Hotkeys.cs</c>).
public sealed partial class DisplaysPageViewModel : PageViewModel, IDisposable
{
    private const string LaptopId = "laptop";
    private const string BuiltIn = "Built-in display";

    /// What a worker thread found out about the displays, to be shown on the UI thread.
    private sealed record Snapshot(IReadOnlyList<DisplayInfo> Displays, IReadOnlyList<IMonitor> Monitors, bool HasLaptop);

    private readonly IMonitorEngine _engine;
    private readonly IActionRegistry _hotkeys;
    private readonly Func<string, SuggestedKeysSwitch> _suggestedKeys;
    private readonly IStateRegistry _states;
    private readonly IOsd _osd;
    private readonly SoftwareDisplayService _software;
    private readonly Action<string> _report;
    private readonly LevelStepper _levels = new();
    private readonly ThrottledRun _rescan;
    private readonly SoftwareGlobalItem _global;
    private readonly WarmLightItem _warm;
    private readonly WarmLightScheduler _scheduler;
    private readonly DisplaySettingsStore _store;
    private readonly PicturePresetBook _pictureBook;
    private readonly ProviderChoice _presets;
    private readonly MonitorPresetBook _monitorBook;
    private readonly ProviderChoice _monitorPresets;
    private readonly List<HardwareDisplayItem> _hardware = new();
    private readonly IHardwareWatches _watches;
    private readonly SystemScreens _systemScreens;
    private readonly List<SoftwareDisplayItem> _softwareItems = new();
    private IReadOnlyList<IMonitor> _shownMonitors = Array.Empty<IMonitor>();
    private bool _hasLaptop;

    public override string Title => "Displays";

    public override string? NavGroup => NavGroups.Devices;

    public override string KeyHelp => "F5 looks at the monitors again (Refresh).";
    public override string Icon => "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M4,6 V16 H20 V6 Z";
    public override string Keywords => "monitor display screen brightness contrast ddc laptop panel gamma digital saturation temperature warm cool input source colour color";

    /// What the monitors themselves can do (DDC/CI and the laptop panel): one inner group per display.
    public ParentGroup Hardware { get; } = new("Displays/Hardware", DisplayControls.HardwareGroup, NoMonitors)
    {
        SummaryBesideName = true,
        ExpandedByDefault = true,
        Tip = "Talks to each monitor over DDC/CI (it must be on in the monitor's own menu); controls a monitor does not report are greyed out. "
              + "Turn screen off sends it to sleep: wake it with its button or by using its input, as the monitor allows.",
    };

    /// What OmniControl does to the picture itself: the whole-desktop controls, then one inner group per display.
    public ParentGroup Software { get; }

    /// The preset manager's ids of the two preset groups (also part of their shortcut actions' ids: never rename).
    public const string HardwarePresetsId = "displays.hardware", SoftwarePresetsId = "displays.software";

    private readonly Action<string?, string?> _openPresets;

    /// Only the controls a monitor has are searched for; the ones it does not report are not offered.
    public override IEnumerable<SearchEntry> SearchEntries =>
        _hardware.SelectMany(h => h.AvailableCards.Select(c => Entry(c, $"{c.Label} ({h.Name})", $"monitor display screen ddc {h.Name}", DisplayControls.HardwareGroup)))
            .Concat(SoftwareWholeCards.Select(c => Entry(c, c.Label, "monitor display screen digital software", DisplayControls.SoftwareGroup)))
            .Concat(_softwareItems.SelectMany(s => s.Cards.Select(c =>
                Entry(c, $"{c.Label} ({s.Name})", $"monitor display screen digital software {s.Name}", DisplayControls.SoftwareGroup))))
            .Append(Entry(PresetChoice, "Display presets", "display presets manage save apply picture preset cogwheel settings", DisplayControls.SoftwareGroup))
            .Append(Entry(MonitorPresetChoice, "Monitor presets", "monitor presets manage save apply hardware ddc preset cogwheel settings", DisplayControls.HardwareGroup));

    private static string NoMonitors => "No monitors available";

    /// <param name="suggestedKeys">Makes the "Suggested keys" switch of a set of actions (the orientation cards' rotation keys).</param>
    /// <param name="presets">The master list of preset groups: the page registers its two.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group (the header cogwheels).</param>
    /// <param name="states">The master list of states: each display's levels are registered there while it is attached.</param>
    /// <param name="hardware">The master list of hardware watches: the page registers its two (the monitors, and the list of displays).</param>
    public DisplaysPageViewModel(IMonitorEngine engine, IActionRegistry hotkeys, Func<string, SuggestedKeysSwitch> suggestedKeys, IOsd osd, SoftwareDisplayService software, Action<string> report,
        IPresetGroups presets, Action<string?, string?> openPresets, IStateRegistry states, IHardwareWatches hardware)
    {
        (_engine, _hotkeys, _suggestedKeys, _osd, _software, _report, _openPresets, _states) = (engine, hotkeys, suggestedKeys, osd, software, report, openPresets, states);
        _rescan = new ThrottledRun(Timing.DisplayRescanGap, RescanNow);
        _watches = hardware;
        _systemScreens = new SystemScreens(hardware);
        RegisterWatches(hardware);
        hardware.ScreensChanged += OnScreensChanged;

        foreach (var spec in DisplayControls.All) osd.Declare(spec.Osd, spec.Icon);

        _store = DisplaySettingsStore.OnDisk();
        _scheduler = new WarmLightScheduler(software, _store);
        // Order matters: a preset drop-down reads what it lists as soon as it is made. The store comes first; the global item comes before
        // the picture drop-down (it reads the picture, including it); _hardware (a field initialiser, empty until the displays are
        // read) is what the monitor drop-down reads, and it greys itself until a monitor reports a control. The two preset groups are
        // registered after the page's own actions, once both books exist: registering may read their presets at once (a preset with a
        // saved shortcut gets its action, listed after the page's own).
        _global = new SoftwareGlobalItem(software, osd, Title, report, ResetAllSoftware, RefreshPictureChoice);
        _pictureBook = new PicturePresetBook(_store, CapturePicture, ApplyPicture);
        var pictureGroup = new BookPresets(_pictureBook);
        _presets = new ProviderChoice(_pictureBook.Label, pictureGroup, ManagePicturePresets, withCustom: true);
        _monitorBook = new MonitorPresetBook(_store, PresetTargets);
        var monitorGroup = new BookPresets(_monitorBook);
        _monitorPresets = new ProviderChoice(_monitorBook.Label, monitorGroup, ManageMonitorPresets, withCustom: true);
        _warm = new WarmLightItem(_scheduler, _store, Title, report);
        Software = new ParentGroup("Displays/Software", DisplayControls.SoftwareGroup, string.Empty, SoftwareWholeCards.Cast<object>().ToList())
        {
            ExpandedByDefault = true,
            Restore = ResetAllSoftware,
            RestoreTip = "Puts every display's software picture, saturation, grayscale, invert and the warm-light schedule back to normal. Presets are kept.",
            Tip = "Applied by OmniControl on any display and put back when it closes (the saved presets and the warm-light schedule are kept). "
                  + "Colour temperature: minus is warmer, plus is cooler. Saturation, grayscale and invert apply to all displays. "
                  + "A display whose driver does not accept colour changes from programs is greyed out.",
        };
        Hardware.UseSwitch(ScreenMaster());
        RegisterActions();
        presets.Register(new PresetGroup(HardwarePresetsId, Title, DisplayControls.HardwareGroup, monitorGroup));
        presets.Register(new PresetGroup(SoftwarePresetsId, Title, DisplayControls.SoftwareGroup, pictureGroup));
        _scheduler.Changed += ShowRefusals;
        RegisterPageStates();
        _scheduler.Start();
        Task.Run(RefreshNow);
    }

    private void RefreshPictureChoice() => _presets.Refresh();

    /// The software cards that belong to every display: the desktop's colour effect and the warm-light schedule.
    private IEnumerable<IEditorItem> SoftwareWholeCards => _global.Cards.Concat(_warm.Cards);

    // ---------------------------------------------------------------- picture presets

    /// The preset drop-down on the Software group's title line (the presets window is its cogwheel).
    public ChoiceItem PresetChoice => _presets.Choice;

    /// The cogwheel beside the picture preset drop-down: the preset manager at the Software card.
    public void ManagePicturePresets() => _openPresets(Title, SoftwarePresetsId);

    /// The software picture every display and the desktop show now.
    private DisplayPreset CapturePicture()
    {
        var preset = new DisplayPreset { Saturation = _global.Saturation, Invert = _global.Invert };
        foreach (var item in _softwareItems) preset.Displays[item.Id] = item.Values;
        preset.Any = _softwareItems.FirstOrDefault()?.Values ?? new PictureValues();
        return preset;
    }

    private void ApplyPicture(DisplayPreset preset)
    {
        foreach (var item in _softwareItems) item.Show(preset.For(item.Id));
        _global.Show(preset.Saturation, preset.Invert);
    }

    /// The "next display preset" action: the saved preset after the one applied last (the first one at the start).
    private void NextPreset()
    {
        if (_store.Presets.Count == 0) return;
        _lastPreset = (_lastPreset + 1) % _store.Presets.Count;
        ApplyPicture(_store.Presets[_lastPreset]);
    }

    private int _lastPreset = -1;

    /// A change (the schedule's, or a slider's) may have shown that a display's driver takes no colour changes: grey its cards out.
    private void ShowRefusals()
    {
        foreach (var item in _softwareItems) item.ShowRefusal();
    }

    // ---------------------------------------------------------------- looking at the displays again

    public override void OnShown() => _rescan.Request();

    public override void OnWindowShownChanged(bool shown) => _rescan.Request();

    /// Opens every monitor again and asks each what it supports (the Refresh button, F5).
    [RelayCommand]
    private void Reload() => Task.Run(() => Guard.Run("Displays: Refresh", ReloadNow));

    /// The Refresh button's work (worker thread), also the monitors' hardware watch: open every monitor again, show them, and return once
    /// each has answered what it supports (the cards fill in as the answers come), so the watch's check sees the real answers.
    private void ReloadNow()
    {
        _engine.RefreshMonitors();
        Dispatcher.UIThread.Post(() =>
        {
            _software.ClearRefusals(); // a driver that refused may accept now: try again
            ShowRefusals();
        });
        RefreshNow();
        ProbeMonitors();
    }

    /// The throttled check (worker thread): only when the attached displays differ from the last look is anything redone.
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

    /// Shows the engine's current displays (worker thread), whatever Windows says. The laptop panel is asked through WMI, which can fail
    /// in any way while a session changes (a COM or access error, not only a WMI one): that failure is logged and the monitors are shown
    /// without the panel, never left unshown.
    private void RefreshNow()
    {
        var hasLaptop = false;
        Guard.Run("Displays: read the built-in panel", () =>
        {
            var laptop = _engine.Laptop;
            hasLaptop = laptop.IsAvailable && laptop.GetBrightness(out _, out var max).Success && max > 0;
        });
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
        Hardware.RefreshMaster(); // with screens that have a power switch the summary reads "N of M on"
    }

    /// A monitor that is also the laptop's panel is shown once, as the laptop panel (its brightness is Windows').
    private void ShowHardware(Snapshot snapshot)
    {
        var monitors = snapshot.Monitors.Where(m => !(snapshot.HasLaptop && m.Info.IsInternal)).ToList();
        if (monitors.SequenceEqual(_shownMonitors) && _hardware.Any(h => h.IsLaptopPanel) == snapshot.HasLaptop) return; // nothing changed

        foreach (var old in _hardware)
        {
            old.Dispose();
            ForgetStates(old);
        }
        _hardware.Clear();
        Hardware.Subgroups.Clear();
        _shownMonitors = monitors;

        if (snapshot.HasLaptop)
        {
            var panel = snapshot.Displays.FirstOrDefault(d => d.IsInternal);
            Add(new HardwareDisplayItem(LaptopId, BuiltIn, new LaptopAccess(_engine.Laptop), Orientation(panel, BuiltIn), _levels, _osd, _systemScreens, _report, ShowHardwareGroups, OnDisplayValues));
        }
        foreach (var monitor in monitors)
            Add(new HardwareDisplayItem(monitor.Info.DeviceName, monitor.Info.Name, new MonitorAccess(monitor), Orientation(monitor.Info, monitor.Info.Name),
                _levels, _osd, _systemScreens, _report, ShowHardwareGroups, OnDisplayValues));
    }

    private DisplayOrientationCard Orientation(DisplayInfo? display, string name) => new(display, _engine.ResolveDeviceName, name, _engine.Rotation, _report, _suggestedKeys(RotationKeySet));

    private void Add(HardwareDisplayItem display)
    {
        _hardware.Add(display);
        RegisterStates(display);
        ShowHardwareGroups();
        display.Start();
    }

    /// Lists every display's groups in order (a display's change after it was read replaces its groups).
    private void ShowHardwareGroups()
    {
        Hardware.Subgroups.Clear();
        foreach (var group in _hardware.SelectMany(h => h.Groups)) Hardware.Subgroups.Add(group);
        SyncPowerStates();
        OnDisplayValues(); // a monitor that was read, came or went changes what a monitor preset shows and whether one can be saved
    }

    /// Software groups follow the attached displays: new ones appear, gone ones are forgotten, the others keep their values.
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
            item ??= new SoftwareDisplayItem(display, _software, _report, _presets.Refresh);
            fresh.Add(item);
        }

        if (!fresh.SequenceEqual(_softwareItems))
        {
            _softwareItems.Clear();
            _softwareItems.AddRange(fresh);
            Software.Subgroups.Clear();
            foreach (var item in fresh) Software.Subgroups.Add(item.Group);
        }
        _presets.Refresh(); // a display that came or went changes what the picture is
    }

    /// Every display's software picture, the colour effect and the warm-light schedule back to normal (the saved presets are kept).
    private void ResetAllSoftware()
    {
        foreach (var item in _softwareItems) item.Reset();
        _global.Reset();
        _warm.Reset();
        _software.ResetAll();
        _presets.Refresh();
    }

    public void Dispose()
    {
        _scheduler.Changed -= ShowRefusals;
        _watches.ScreensChanged -= OnScreensChanged;
        ForgetPageStates();
        _warm.Dispose();
        _presets.Dispose();
        _monitorPresets.Dispose();
        _monitorBook.Dispose();
        _scheduler.Dispose();
        _rescan.Dispose();
        foreach (var display in _hardware)
        {
            display.Dispose();
            ForgetStates(display);
        }
    }
}
