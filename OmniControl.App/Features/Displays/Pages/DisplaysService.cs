using OmniControl.App.Shared.Contracts;
using System.ComponentModel;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Shared.Presets;
using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// The Displays page's service part, made by the module at start-up (the page, <see cref="DisplaysPageViewModel"/>, and its cards are made
/// at its first use): the displays as models, what each monitor itself can do (over DDC/CI, and a laptop's panel) and what OmniControl does
/// to the picture on any display, which the actions, states, presets and hardware watches act on. The displays are looked at again,
/// cheaply, when Windows reports a display change, and while the page exists whenever the window is shown or hidden. The actions are one
/// per control, not per display (<c>DisplaysService.Hotkeys.cs</c>).
public sealed partial class DisplaysService : IDisposable
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
    private readonly DesktopPictureModel _desktop;
    private readonly WarmLightService _warmLight;
    private readonly DisplaySettingsStore _store;
    private readonly PicturePresetBook _pictureBook;
    private readonly MonitorPresetBook _monitorBook;
    private readonly List<HardwareDisplayModel> _hardware = new();
    private readonly IHardwareWatches _watches;
    private readonly SystemScreens _systemScreens;
    private readonly List<SoftwareDisplayModel> _softwareDisplays = new();
    private IReadOnlyList<IMonitor> _shownMonitors = Array.Empty<IMonitor>();

    /// The page's title: the category of its actions, states and presets.
    private string Title { get; }

    /// The preset manager's ids of the two preset groups (also part of their shortcut actions' ids: never rename).
    public const string HardwarePresetsId = "displays.hardware", SoftwarePresetsId = "displays.software";

    private readonly Action<string?, string?> _openPresets;

    /// The displays' monitors (each one's own controls), the desktop's picture, the warm-light schedule and each display's software picture.
    internal IReadOnlyList<HardwareDisplayModel> HardwareDisplays => _hardware;
    internal DesktopPictureModel Desktop => _desktop;
    internal WarmLightModel WarmLight => _warmLight.Model;
    internal IReadOnlyList<SoftwareDisplayModel> SoftwareDisplays => _softwareDisplays;

    /// The two preset groups (the page's header drop-downs read them) and what the drop-downs are called.
    internal IPresetProvider PicturePresets { get; }
    internal IPresetProvider MonitorPresets { get; }
    internal string PicturePresetsLabel => _pictureBook.Label;
    internal string MonitorPresetsLabel => _monitorBook.Label;

    /// The monitors came, went or one was read.
    internal event Action? HardwareListChanged;

    /// A monitor's value or a screen's power changed.
    internal event Action? HardwareValuesChanged;

    /// The software displays came or went.
    internal event Action? SoftwareListChanged;

    /// The software picture changed (what a picture preset shows).
    internal event Action? PictureChanged;

    /// <param name="context">The master lists and openers the page uses: its actions, states, two preset groups, two hardware watches (the
    /// monitors, and the list of displays), the OSD, the rotation keys' suggested-keys switch, the preset manager, the status line and the rules.</param>
    /// <param name="title">The page's title (its actions', states' and presets' category).</param>
    public DisplaysService(IMonitorEngine engine, SoftwareDisplayService software, FeatureContext context, string title)
    {
        (_engine, _hotkeys, _suggestedKeys, _osd, _software, _report, _openPresets, _states, Title) =
            (engine, context.Actions, context.SuggestedKeys, context.Osd, software, context.Report, context.OpenPresetManager, context.States, title);
        var hardware = context.Hardware;
        _rescan = new ThrottledRun(Timing.DisplayRescanGap, RescanNow);
        _watches = hardware;
        _systemScreens = new SystemScreens(hardware);
        RegisterWatches(hardware);
        hardware.ScreensChanged += OnScreensChanged;

        foreach (var spec in DisplayControls.All) _osd.Declare(spec.Osd, spec.Icon);

        // Order matters: the store comes first, the models before the books that read them. The two preset groups are registered after the
        // page's own actions: registering may read their presets at once (a preset with a saved shortcut gets its action, listed after the page's own).
        _store = DisplaySettingsStore.OnDisk();
        _desktop = new DesktopPictureModel(software, _report, OnPictureChanged);
        _pictureBook = new PicturePresetBook(_store, CapturePicture, ApplyPicture);
        PicturePresets = new BookPresets(_pictureBook);
        _monitorBook = new MonitorPresetBook(_store, PresetTargets);
        MonitorPresets = new BookPresets(_monitorBook);
        RegisterActions();
        _warmLight = new WarmLightService(software, _store, context.Rules, _hotkeys, _states, title);
        context.Presets.Register(new PresetGroup(HardwarePresetsId, Title, DisplayControls.HardwareGroup, MonitorPresets));
        context.Presets.Register(new PresetGroup(SoftwarePresetsId, Title, DisplayControls.SoftwareGroup, PicturePresets));
        _warmLight.Model.Changed += ShowRefusals;
        Guard.OffUi("Displays: reading the displays", RefreshNow);
    }

    /// The status line (the cards report what failed through it).
    internal Action<string> Report => _report;

    private void OnPictureChanged() => PictureChanged?.Invoke();

    /// Makes the switch of the orientation hotkeys' suggested keys for an Orientation card (the card owns it).
    internal SuggestedKeysSwitch NewRotationKeysSwitch() => _suggestedKeys(RotationKeySet);

    // ---------------------------------------------------------------- picture presets

    /// The cogwheel beside the picture preset drop-down: the preset manager at the Software card.
    internal void ManagePicturePresets() => _openPresets(Title, SoftwarePresetsId);

    /// The software picture every display and the desktop show now.
    private DisplayPreset CapturePicture()
    {
        var preset = new DisplayPreset { Saturation = _desktop.Saturation, Invert = _desktop.Invert };
        foreach (var display in _softwareDisplays) preset.Displays[display.Id] = display.Values;
        preset.Any = _softwareDisplays.FirstOrDefault()?.Values ?? new PictureValues();
        return preset;
    }

    private void ApplyPicture(DisplayPreset preset)
    {
        foreach (var display in _softwareDisplays) display.Show(preset.For(display.Id));
        _desktop.Show(preset.Saturation, preset.Invert);
    }

    /// The "next display preset" action: the saved preset after the one applied last (the first one at the start).
    private void NextPreset()
    {
        if (_store.Presets.Count == 0) return;
        _lastPreset = (_lastPreset + 1) % _store.Presets.Count;
        ApplyPicture(_store.Presets[_lastPreset]);
    }

    private int _lastPreset = -1;

    /// A change (the schedule's, or a slider's) may have shown that a display's driver takes no colour changes.
    private void ShowRefusals()
    {
        foreach (var display in _softwareDisplays) display.ShowRefusal();
    }

    // ---------------------------------------------------------------- looking at the displays again

    /// Looks whether the attached displays changed (cheap, throttled; the page asks when it shows and when the window shows or hides).
    public void Rescan() => _rescan.Request();

    /// Opens every monitor again and asks each what it supports (the Refresh button, F5).
    public void Reload() => Task.Run(() => Guard.Run("Displays: Refresh", ReloadNow));

    /// The Refresh button's work (worker thread), also the monitors' hardware watch: open every monitor again, show them, and return once
    /// each has answered what it supports (the models fill in as the answers come), so the watch's check sees the real answers.
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
        ShowHardware(snapshot);
        ShowSoftware(snapshot.Displays);
        RefreshActionDevices();
        HardwareValuesChanged?.Invoke(); // with screens that have a power switch the summary reads "N of M on"
    }

    /// A monitor that is also the laptop's panel is shown once, as the laptop panel (its brightness is Windows').
    private void ShowHardware(Snapshot snapshot)
    {
        var monitors = snapshot.Monitors.Where(m => !(snapshot.HasLaptop && m.Info.IsInternal)).ToList();
        if (monitors.SequenceEqual(_shownMonitors) && _hardware.Any(h => h.IsLaptopPanel) == snapshot.HasLaptop) return; // nothing changed

        foreach (var old in _hardware) Forget(old);
        _hardware.Clear();
        _shownMonitors = monitors;

        if (snapshot.HasLaptop)
        {
            var panel = snapshot.Displays.FirstOrDefault(d => d.IsInternal);
            Add(new HardwareDisplayModel(LaptopId, BuiltIn, new LaptopAccess(_engine.Laptop), Orientation(panel, BuiltIn), _levels, _osd, _systemScreens, _report, DisplayRead));
        }
        foreach (var monitor in monitors)
            Add(new HardwareDisplayModel(monitor.Info.DeviceName, monitor.Info.Name, new MonitorAccess(monitor), Orientation(monitor.Info, monitor.Info.Name),
                _levels, _osd, _systemScreens, _report, DisplayRead));
        if (_hardware.Count == 0) DisplayRead();
    }

    private DisplayOrientationModel Orientation(DisplayInfo? display, string name) => new(display, _engine.ResolveDeviceName, name, _engine.Rotation, _report);

    private void Add(HardwareDisplayModel display)
    {
        _hardware.Add(display);
        display.Changed += OnDisplayValues;
        RegisterStates(display);
        DisplayRead();
        display.Start();
    }

    private void Forget(HardwareDisplayModel display)
    {
        display.Changed -= OnDisplayValues;
        display.Dispose();
        ForgetStates(display);
    }

    /// A display came, went or was read: its power state, the page's groups and what a monitor preset shows follow.
    private void DisplayRead()
    {
        SyncPowerStates();
        HardwareListChanged?.Invoke();
        OnDisplayValues();
    }

    /// Software displays follow the attached displays: new ones appear, gone ones are forgotten, the others keep their values.
    private void ShowSoftware(IReadOnlyList<DisplayInfo> displays)
    {
        foreach (var gone in _softwareDisplays.Where(s => displays.All(d => d.DeviceName != s.Id)).ToList())
        {
            gone.Forget();
            _softwareDisplays.Remove(gone);
        }

        var fresh = new List<SoftwareDisplayModel>();
        foreach (var display in displays)
            fresh.Add(_softwareDisplays.FirstOrDefault(s => s.Id == display.DeviceName) ?? new SoftwareDisplayModel(display, _software, OnPictureChanged));

        if (!fresh.SequenceEqual(_softwareDisplays))
        {
            _softwareDisplays.Clear();
            _softwareDisplays.AddRange(fresh);
            SoftwareListChanged?.Invoke();
        }
        OnPictureChanged(); // a display that came or went changes what the picture is
    }

    /// Every display's software picture, the colour effect and the warm-light schedule back to normal (the saved presets are kept).
    internal void ResetAllSoftware()
    {
        foreach (var display in _softwareDisplays) display.Reset();
        _desktop.Reset();
        _warmLight.Model.Reset();
        _software.ResetAll();
        OnPictureChanged();
    }

    public void Dispose()
    {
        _warmLight.Model.Changed -= ShowRefusals;
        _watches.ScreensChanged -= OnScreensChanged;
        _monitorBook.Dispose();
        _warmLight.Dispose();
        _rescan.Dispose();
        foreach (var display in _hardware) Forget(display);
    }
}
