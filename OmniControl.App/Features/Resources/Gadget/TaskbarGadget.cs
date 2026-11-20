using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Resources.Weather;
using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Gadget;

/// The gadget as the Taskbar gadget tab sees it: shown or not, the cells of any look, the taskbar's colour and text colour (read once per Windows
/// colour change), a hold, a change.
public interface IGadgetPreview
{
    bool IsShown { get; }

    void SetShown(bool on);

    /// <param name="sampleUnknown">A reading or weather value that is unknown or 0 shows a sample (<see cref="Reading.SampleOr"/>,
    /// <see cref="Weather.WeatherWords.Sampled"/>; the preview and the layout tiles), never "—" or "0".</param>
    IReadOnlyList<GadgetCell> Cells(GadgetSettings look, bool sampleUnknown = false);

    Color Background { get; }

    Color Ink { get; }

    IDisposable Hold();

    event Action? Changed;

    /// THE live list of readings (the disks' included), whose rows the tab lists.
    IReadingList Readings { get; }
}

/// The taskbar gadget: the chosen readings and the weather left of the tray (<see cref="GadgetWindow"/>), placed after every read and on taskbar changes.
public sealed class TaskbarGadget : IGadgetPreview, IDisposable
{
    public const string StateId = "resources.gadget";
    private const string ToggleId = "resources.gadget.toggle", WatchId = "resources.gadget";

    private readonly ResourceMonitor _monitor;
    private readonly WeatherService _weather;
    private readonly ResourcesSettings _settings;
    private readonly IActionRunner _runner;
    private readonly IStateRegistry _states;
    private readonly Action<string> _report;
    private readonly IResourcesWindows _windows;
    private readonly GadgetLayouts _layouts;
    private readonly HoldCount _preview = new();
    private GadgetWindow? _window;
    private TaskbarPlace? _placed;

    /// <param name="layouts">The layouts the right-click menu lists and applies.</param>
    /// <param name="windows">Opens the page at its Network by app group and at its Taskbar gadget tab (the right-click menu's links).</param>
    public TaskbarGadget(ResourceMonitor monitor, WeatherService weather, ResourcesSettings settings, GadgetLayouts layouts, IActionRunner runner,
        IStateRegistry states, Action<string> report, IResourcesWindows windows)
    {
        (_monitor, _weather, _settings, _layouts, _runner, _states, _report, _windows) = (monitor, weather, settings, layouts, runner, states, report, windows);
        monitor.Updated += Draw;
        monitor.ReadingsChanged += AddRows;
        weather.Updated += Draw;
        settings.Changed += Apply;
        _preview.Changed += NeedReadings;
        _platform = Application.Current?.PlatformSettings;
        if (_platform is not null) _platform.ColorValuesChanged += OnWindowsColours;
        Apply();
    }

    /// Its state, its On / Off / Toggle actions and its hardware watch in the master lists.
    public void Register(IActionRegistry actions, IHardwareWatches hardware, string category)
    {
        _states.Register(new StateEntry(StateId, "Taskbar gadget", category, StateKind.OnOff, () => StateValue.Of(IsShown), "Shown or hidden here.",
            Keywords: "resources gadget taskbar readout", Set: v => SetShown(v.Number is > 0), Icon: "device-desktop-analytics"));
        var family = new OnOffFamily(ToggleId, "Taskbar gadget", StateId);
        var toggle = new HotkeyAction(ToggleId, "Taskbar gadget: Toggle", category, "resources gadget taskbar show hide readout", _ => SetShown(!IsShown))
        {
            Family = family.Family, FamilyRole = OnOffFamily.ToggleRole, SuggestedIcons = new[] { "device-desktop-analytics" },
        };
        actions.Register(family.Siblings(toggle, SetShown).Prepend(toggle));
        hardware.Register(new HardwareWatch(WatchId, "Taskbar gadget", HardwareSignal.Taskbar | HardwareSignal.Display | HardwareSignal.Session,
            () => Dispatcher.UIThread.Invoke(Place),
            LooksStale: () => IsShown && (_placed is not { } placed || (_size is var (w, h) && TaskbarSpot.Find(w, h, Look.SpaceToTray) != placed))));
    }

    private (int, int) _size;
    private int _dpi; // the taskbar's DPI when last placed: a child window is not told when its screen's scale changes
    private bool _owned, _hidden; // the window is owned by the taskbar, not its child; hidden over a full-screen app
    private readonly Avalonia.Platform.IPlatformSettings? _platform;
    private bool? _taskbarLight; // read once, and again when Windows' colours change (the taskbar's own text colour follows its theme)

    private void OnWindowsColours(object? sender, Avalonia.Platform.PlatformColorValues e)
    {
        _taskbarLight = null;
        Draw();
    }

    private GadgetSettings Look => _settings.Setup.Gadget;

    public IReadingList Readings => _monitor;

    /// A disk came or went: a new disk's rows join the list (hidden) and are saved, and the gadget reads and draws what is shown now.
    private void AddRows()
    {
        var rows = Look.Readings.Count;
        Apply();
        if (Look.Readings.Count != rows) _settings.Save();
    }

    private bool TaskbarLight => _taskbarLight ??= TaskbarSpot.IsLight();

    public bool IsShown => Look.Shown;

    public void SetShown(bool on)
    {
        if (on == IsShown) return;
        Look.Shown = on;
        _settings.Commit();
    }

    /// Shows, hides or redraws the gadget after a change of its settings.
    public void Apply()
    {
        Look.Complete(_monitor.Readings); // this PC's disks keep their rows whatever a preset, layout or import brought
        _states.Notify(StateId);
        NeedReadings();
        if (!Look.Shown) CloseWindow();
        else if (_window is null)
        {
            var window = _window = new GadgetWindow(MenuRows);
            window.Clicked += () => Run(Look.ClickAction);
            window.DoubleClicked += () => Run(Look.DoubleClickAction);
            // A child window is destroyed with the taskbar when Explorer restarts; TaskbarCreated then opens a new one through Place.
            window.Closed += (_, _) =>
            {
                if (_window == window) Forget();
            };
            window.Show();
        }
        Draw();
    }

    /// Reads at the gadget's interval while shown (its shown readings) or previewed (every reading: the layout cards show other looks).
    private void NeedReadings() =>
        _monitor.Need(ResourceMonitor.GadgetNeed, Look.Shown || _preview.Held ? TimeSpan.FromSeconds(Math.Max(Look.ReadEverySeconds, Timing.ResourcesGadgetReadMin.TotalSeconds)) : null,
            _preview.Held ? SampleParts.All : Reading.PartsOf(Look.Readings.Where(r => r.Shown).Select(r => _monitor.Find(r.Id)).OfType<Reading>()));

    private void CloseWindow()
    {
        _window?.Close();
        Forget();
    }

    private void Forget() => (_window, _placed, _hidden) = (null, null, false);

    private void Run(string actionId)
    {
        if (actionId.Length > 0 && !_runner.Run(actionId)) _report("The gadget's click action is not available on this PC: choose another on the Resources page's Taskbar gadget tab.");
    }

    public Color Background => Color.Parse(TaskbarLight ? Palette.LightHex : Palette.CharcoalHex);

    /// The taskbar's own text colour: dark on a light taskbar.
    public Color Ink => TaskbarLight ? Palette.Ink : Palette.InkOnDark;

    public IDisposable Hold() => _preview.Take();

    public event Action? Changed;

    /// The cells <paramref name="look"/> shows now: each shown reading as "label value" (warn colour past its point), then the weather's parts; each with
    /// its hover tooltip as the look's hover details ask (the reading under the pointer or all shown readings; the weather's own).
    public IReadOnlyList<GadgetCell> Cells(GadgetSettings look, bool sampleUnknown = false)
    {
        var text = ColourOf(look.TextColour, Ink);
        var hover = look.Hover;
        var shownReadings = look.Readings.Where(r => r.Shown && r.Id != GadgetSettings.WeatherId).ToList();
        var cells = new List<GadgetCell>();
        foreach (var shown in look.Readings.Where(r => r.Shown))
        {
            if (shown.Id == GadgetSettings.WeatherId)
            {
                cells.AddRange(_weather.Cells(shown, look.Weather.Shows, text, hover, sampleUnknown));
                continue;
            }
            if (_monitor.Find(shown.Id) is not { } reading) continue;
            var warm = shown.WarnAt is { } at && reading.InUnit(_monitor.Latest) >= at;
            var value = sampleUnknown ? reading.SampleOr(reading.Read(_monitor.Latest)) : reading.Read(_monitor.Latest);
            cells.Add(new GadgetCell(shown.Label, reading.GadgetWords(value, look), warm ? ColourOf(shown.Colour, text) : text,
                reading.GadgetWidest(look), shown.GapAfter,
                hover.Readings ? () => ReadingTips.Lines(_monitor, hover.AllReadings ? shownReadings : new[] { shown }, hover.ReadingShows) : null, warm));
        }
        return cells;
    }

    private void Draw()
    {
        Changed?.Invoke();
        if (_window is null) return;
        var look = Look;
        _window.HoverOutline = look.HoverOutline;
        _window.Draw(Cells(look), GadgetLayout.Of(look));
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded); // after the window has taken its new size
    }

    /// A stored gadget colour; "" is <paramref name="text"/> (the text colour, or the taskbar's own <see cref="Ink"/>).
    public static Color ColourOf(string stored, Color text) => stored.Length > 0 ? Color.Parse(stored) : text;

    /// Puts the gadget into the taskbar left of the tray (again after Explorer restarts or the scale changes); moves nothing while nothing changed. UI thread.
    private void Place()
    {
        if (_window is null)
        {
            if (IsShown && TaskbarSpot.Find(0, 0, 0) is not null) Apply();
            return;
        }
        if (_window.TryGetPlatformHandle() is not { } handle) return;
        _size = _window.PixelSize;
        var (w, h) = _size;
        if (TaskbarSpot.Find(w, h, Look.SpaceToTray) is not { } place) return;
        var dpi = TaskbarSpot.Dpi(place.Taskbar);
        if (dpi != _dpi && _placed is not null && Math.Abs(_window.RenderScaling * 96 - dpi) >= 1)
        {
            _dpi = dpi;
            CloseWindow(); // a window opened now takes the new scale; it is put into the taskbar like the first
            Apply();
            return;
        }
        _dpi = dpi;
        if (_placed?.Taskbar != place.Taskbar) _owned = !TaskbarSpot.Attach(handle.Handle, place.Taskbar);
        if (_placed != place || TaskbarSpot.IsCovered(handle.Handle)) TaskbarSpot.Put(handle.Handle, place);
        _placed = place;
        // A child hides with the taskbar; an owned window (Windows refused the child) hides itself while a full-screen app is on the
        // taskbar's monitor, looked at on every read (no poll of its own).
        var hide = _owned && TaskbarSpot.FullScreenOn(place.Taskbar);
        if (hide == _hidden) return;
        _hidden = hide;
        TaskbarSpot.Show(handle.Handle, !hide);
    }

    /// The right-click menu: Layout (the favourite layouts) and Show (every reading, ticked) as sub-menus, the gadget's settings, Network by app
    /// and Hide.
    private IReadOnlyList<MenuRowItem> MenuRows() => new[]
    {
        MenuRowItem.Sub("Layout", "layout-dashboard", LayoutRows, _layouts.CurrentName),
        MenuRowItem.Sub("Show", "eye", ShowRows),
        Link("Gadget settings…", "settings", _windows.OpenGadgetSettings, ruleAbove: true),
        Link("Network by app…", "network", _windows.OpenApps),
        new MenuRowItem("Hide gadget", string.Empty, false, string.Empty, new RelayCommand(() => SetShown(false)), RuleAbove: true, IsMuted: true, Icon: "eye-off"),
    };

    private static MenuRowItem Link(string text, string icon, Action open, bool ruleAbove = false) =>
        new(text, string.Empty, false, string.Empty, new RelayCommand(open), RuleAbove: ruleAbove, Icon: icon);

    /// The favourite layouts (the one in use ticked), then the way to all of them.
    private IReadOnlyList<MenuRowItem> LayoutRows()
    {
        var rows = _layouts.FavouriteEntries().Select(e => new MenuRowItem(e.Name, string.Empty, e.InUse, string.Empty, new RelayCommand(() =>
        {
            if (_layouts.Apply(e.Id) is { } problem) _report(problem);
        }))).ToList();
        rows.Add(new MenuRowItem("More layouts…", string.Empty, false, string.Empty, new RelayCommand(_windows.OpenGadgetSettings), RuleAbove: true, IsMuted: true));
        return rows;
    }

    /// Every reading and the weather, ticked while shown; a click toggles it and keeps the menu open.
    private IReadOnlyList<MenuRowItem> ShowRows()
    {
        var rows = new List<MenuRowItem>();
        foreach (var reading in _monitor.Readings)
        {
            var shown = Look.Readings.FirstOrDefault(r => r.Id == reading.Id);
            rows.Add(new MenuRowItem(reading.Name, string.Empty, shown?.Shown == true, string.Empty, new RelayCommand(() =>
            {
                if (shown is null) return;
                shown.Shown = !shown.Shown;
                _settings.Commit();
            }), KeepsOpen: true));
        }
        rows.Add(new MenuRowItem("Weather", string.Empty, _weather.IsShown, string.Empty, new RelayCommand(() => _weather.SetShown(!_weather.IsShown)), KeepsOpen: true));
        return rows;
    }

    public void Dispose()
    {
        _monitor.Updated -= Draw;
        _monitor.ReadingsChanged -= AddRows;
        _weather.Updated -= Draw;
        _settings.Changed -= Apply;
        _preview.Changed -= NeedReadings;
        if (_platform is not null) _platform.ColorValuesChanged -= OnWindowsColours;
        CloseWindow();
    }
}
