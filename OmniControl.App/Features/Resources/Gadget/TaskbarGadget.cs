using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Resources.Weather;
using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Gadget;

/// The gadget as the Taskbar gadget tab sees it: shown or not, its position, the cells of any look, the taskbar's colour and text colour (read once per Windows
/// colour change), a hold, a change.
public interface IGadgetPreview
{
    bool IsShown { get; }

    void SetShown(bool on);

    /// The taskbar's right or left end (one setting for every layout).
    void SetPosition(GadgetPosition position);

    /// The font of every layout ("" = the default), remembered among the fonts picked before.
    void SetFont(string font);

    /// <param name="sampleUnknown">A reading or weather value that is unknown or 0 shows a sample (<see cref="Reading.SampleOr"/>,
    /// <see cref="Weather.WeatherWords.Sampled"/>; the layout tiles), never "—" or "0".</param>
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
    private GadgetClicks? _clicks;
    private TaskbarPlace? _placed;
    private readonly TaskbarSpot.Picture _picture = new();

    /// <param name="layouts">The layouts the right-click menu lists and applies.</param>
    /// <param name="windows">Opens the page at its Network traffic control group and at its Taskbar gadget tab (the right-click menu's links).</param>
    public TaskbarGadget(ResourceMonitor monitor, WeatherService weather, ResourcesSettings settings, GadgetLayouts layouts, IActionRunner runner,
        IStateRegistry states, Action<string> report, IResourcesWindows windows)
    {
        (_monitor, _weather, _settings, _layouts, _runner, _states, _report, _windows) = (monitor, weather, settings, layouts, runner, states, report, windows);
        monitor.Updated += Redraw;
        monitor.ReadingsChanged += AddRows;
        weather.Updated += Redraw;
        settings.Changed += Follow;
        _preview.Changed += NeedReadings;
        Apply();
    }

    private void Redraw() => Guarded("Taskbar gadget: draw", () => Draw(force: false));

    private void Follow() => Guarded("Taskbar gadget: settings", Apply);

    /// The steps that failed and were logged; one failing on every read is logged once, again only after it has worked.
    private readonly HashSet<string> _failing = new();

    /// Runs a draw or placement entry point (a read, a setting, a taskbar announcement, a posted placement): a failure is logged, never thrown into the UI loop.
    private void Guarded(string what, Action step)
    {
        try
        {
            step();
            _failing.Remove(what);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_failing.Add(what)) Guard.Log(what, ex);
        }
    }

    private bool _placePosted;

    /// Places the gadget once the window has taken its new size; draws close together (a read, a setting) place it once.
    private void PlaceSoon()
    {
        if (_placePosted) return;
        _placePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _placePosted = false;
            Guarded("Taskbar gadget: place", Place);
        }, DispatcherPriority.Loaded);
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
        hardware.Register(new HardwareWatch(WatchId, "Taskbar gadget", HardwareSignal.Taskbar | HardwareSignal.TaskbarSettings | HardwareSignal.Display
                | HardwareSignal.Session | HardwareSignal.Resume | HardwareSignal.DisplayPower,
            () =>
            {
                var light = TaskbarSpot.IsLight(); // the registry is read here, off the UI thread
                Dispatcher.UIThread.Invoke(() => Guarded("Taskbar gadget: taskbar changed", () => Refresh(light)));
            },
            LooksStale: () => IsShown && (_placed is not { } placed || (_size is var (w, h) && TaskbarSpot.Find(w, h, Look.SpaceToTray, KnownLeftEnd(placed.Taskbar)) != placed))));
    }

    /// Where the buttons at the taskbar's left end stopped at the last look, and on which taskbar (<see cref="TaskbarButtons.RunEnd"/>).
    private sealed record LeftEnd(IntPtr Taskbar, int End);

    private LeftEnd? _leftEnd;
    private bool _lookingLeft;
    private DateTime _leftLookedAt;

    private int? KnownLeftEnd(IntPtr taskbar) => Look.Position == GadgetPosition.Left && _leftEnd is { } left && left.Taskbar == taskbar ? left.End : null;

    /// On Left: the end of the taskbar's left buttons, as last looked at (null until the first look: placed on the right meanwhile); a placement
    /// looks again, off the UI thread (UI Automation into Explorer), at most every <see cref="Timing.ResourcesGadgetLeftLook"/> (program
    /// buttons come and go), at once after a taskbar announcement or a change of position. UI thread.
    private int? LeftEndFor(IntPtr taskbar)
    {
        if (Look.Position != GadgetPosition.Left) return null;
        var known = KnownLeftEnd(taskbar);
        if (_lookingLeft || DateTime.UtcNow - _leftLookedAt < Timing.ResourcesGadgetLeftLook) return known;
        (_lookingLeft, _leftLookedAt) = (true, DateTime.UtcNow);
        Guard.OffUi("Taskbar gadget: the taskbar's left buttons", () =>
        {
            int? end = null;
            try { end = TaskbarButtons.RunEnd(taskbar); }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _lookingLeft = false;
                    if (end is not { } found || _leftEnd == new LeftEnd(taskbar, found)) return;
                    _leftEnd = new LeftEnd(taskbar, found);
                    PlaceSoon();
                });
            }
        });
        return known;
    }

    private (int, int) _size;
    private int _dpi; // the taskbar's DPI when last placed: a child window is not told when its screen's scale changes
    private bool _owned, _hidden, _disposed; // the window is owned by the taskbar, not its child; hidden over a full-screen app; no window after Dispose
    private bool? _taskbarLight; // read once, and again after each taskbar announcement (the taskbar's own text colour follows Windows' mode)

    /// After a taskbar, display or session announcement: draws again when Windows' mode changed the taskbar's colour, then puts the gadget back.
    private void Refresh(bool light)
    {
        if (_taskbarLight != light)
        {
            _taskbarLight = light;
            Draw();
        }
        _leftLookedAt = default; // the taskbar changed (its alignment too): look at its left buttons again
        Place();
    }

    private GadgetSettings Look => _settings.Setup.Gadget;

    public IReadingList Readings => _monitor;

    /// A disk came or went: a new disk's rows join the list (hidden) and are saved, and the gadget reads and draws what is shown now.
    private void AddRows() => Guarded("Taskbar gadget: disks changed", () =>
    {
        var rows = Look.Readings.Count;
        Apply();
        if (Look.Readings.Count != rows) _settings.Save();
    });

    private bool TaskbarLight => _taskbarLight ??= TaskbarSpot.IsLight();

    public bool IsShown => Look.Shown;

    public void SetShown(bool on)
    {
        if (on == IsShown) return;
        Look.Shown = on;
        _settings.Commit();
    }

    public void SetPosition(GadgetPosition position)
    {
        if (position == Look.Position) return;
        Look.Position = position;
        _leftLookedAt = default; // look at the left buttons now, not at the next interval
        _settings.Commit();
    }

    public void SetFont(string font)
    {
        if (font == Look.Font) return;
        Look.UseFont(font);
        _settings.Commit();
    }

    /// Shows, hides or redraws the gadget after a change of its settings.
    public void Apply()
    {
        Look.Complete(_monitor.Readings); // this PC's disks keep their rows whatever a preset, layout or import brought
        _states.Notify(StateId);
        NeedReadings();
        if (!Look.Shown) CloseWindow();
        // Before Explorer has made the taskbar (OmniControl started first) no window opens: TaskbarCreated opens it through Place.
        else if (_window is null && !_disposed && TaskbarSpot.Exists)
        {
            var window = _window = new GadgetWindow(MenuRows);
            var clicks = _clicks = new GadgetClicks(() => Look, Run);
            window.Pressed += clicks.Pressed;
            window.Released += clicks.Released;
            window.PictureStale += () => Dispatcher.UIThread.Post(() => Guarded("Taskbar gadget: paint", Paint), DispatcherPriority.Loaded); // after the layout
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

    private void Forget()
    {
        _clicks?.Dispose();
        _picture.Dispose();
        (_window, _clicks, _placed, _hidden, _drawn, _paintDue) = (null, null, null, false, null, true);
    }

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
            var words = ColourOf(shown.TextColour, text);
            if (shown.Id == GadgetSettings.WeatherId)
            {
                cells.AddRange(_weather.Cells(shown, look.Weather.Shows, words, hover, sampleUnknown));
                continue;
            }
            if (_monitor.Find(shown.Id) is not { } reading) continue;
            var warm = shown.WarnAt is { } at && reading.InUnit(_monitor.Latest) >= at;
            var value = sampleUnknown ? reading.SampleOr(reading.Read(_monitor.Latest)) : reading.Read(_monitor.Latest);
            cells.Add(new GadgetCell(shown.Label, reading.GadgetWords(value, look), warm ? ColourOf(shown.Colour, text) : words,
                reading.GadgetWidest(look), shown.GapAfter,
                hover.Readings ? () => ReadingTips.Lines(_monitor, hover.AllReadings ? shownReadings : new[] { shown }, hover.ReadingShows) : null, warm));
        }
        return cells;
    }

    /// What the window shows now, to tell a read that changed nothing seen (words, colours, layout, fill) from one that did.
    private sealed record Drawn(IReadOnlyList<GadgetCell> Cells, GadgetLayout Layout, Color? Fill);

    private Drawn? _drawn;
    private bool _paintDue; // the layered picture is out of date
    private (int, int) _paintedSize;
    private DateTime _placedAt;

    private static bool SameCells(IReadOnlyList<GadgetCell> a, IReadOnlyList<GadgetCell> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Label != b[i].Label || a[i].Value != b[i].Value || a[i].Colour != b[i].Colour || a[i].Widest != b[i].Widest
                || a[i].GapAfter != b[i].GapAfter || a[i].Warned != b[i].Warned) return false;
        return true;
    }

    /// <param name="force">After a setting or a taskbar colour change: the tooltips' details may have changed too, which the words do not show.</param>
    private void Draw(bool force = true)
    {
        Changed?.Invoke();
        if (_window is null) return;
        var look = Look;
        _window.HoverOutline = look.HoverOutline;
        var now = new Drawn(Cells(look), GadgetLayout.Of(look), FillOf(look.Background, Background));
        if (!force && _drawn is { } last && last.Layout == now.Layout && last.Fill == now.Fill && SameCells(last.Cells, now.Cells))
        {
            // The window's size cannot have changed, so it is looked at only as often as the left buttons are (a tray that grew; Windows' own announcements place it at once).
            if (DateTime.UtcNow - _placedAt >= Timing.ResourcesGadgetLeftLook) PlaceSoon();
            return;
        }
        _drawn = now;
        _window.Fill(now.Fill);
        _window.Draw(now.Cells, now.Layout);
        _paintDue = true;
        PlaceSoon();
    }

    /// A stored gadget colour; "" is <paramref name="text"/> (the text colour, or the taskbar's own <see cref="Ink"/>).
    public static Color ColourOf(string stored, Color text) => stored.Length > 0 ? Color.Parse(stored) : text;

    /// The colour a stored <see cref="GadgetSettings.Background"/> fills the gadget with: none (see-through, or a colour that does not read),
    /// <paramref name="taskbar"/> for the taskbar's own, else the colour.
    public static Color? FillOf(string stored, Color taskbar) =>
        stored == GadgetSettings.TaskbarBackground ? taskbar : stored.Length > 0 && Color.TryParse(stored, out var colour) ? colour : null;

    /// Puts the gadget into the taskbar left of the tray, or after its left buttons (again after Explorer restarts or the scale changes); moves
    /// nothing while nothing changed. Runs after every read too, so a tray area that grew or shrank is followed at the next read. UI thread.
    private void Place()
    {
        if (_window is null)
        {
            if (IsShown && TaskbarSpot.Exists) Apply();
            return;
        }
        if (_window.TryGetPlatformHandle() is not { } handle) return;
        _size = _window.PixelSize;
        var (w, h) = _size;
        if (TaskbarSpot.Find(w, h, Look.SpaceToTray, LeftEndFor(TaskbarSpot.Main)) is not { } place) return;
        var dpi = TaskbarSpot.Dpi(place.Taskbar);
        // A new window, put into the taskbar like the first: Explorer restarted (a new taskbar; the old one destroyed the window, or let go
        // of it without Avalonia being told), or the scale changed (a window opened now takes the new scale).
        var anew = TaskbarSpot.IsGone(handle.Handle) || _placed is { } old
            && (old.Taskbar != place.Taskbar || (dpi != _dpi && Math.Abs(_window.RenderScaling * 96 - dpi) >= 1));
        _dpi = dpi;
        if (anew)
        {
            CloseWindow();
            Apply();
            return;
        }
        if (_placed?.Taskbar != place.Taskbar) _owned = !TaskbarSpot.Attach(handle.Handle, place.Taskbar);
        if (_placed != place || TaskbarSpot.IsCovered(handle.Handle)) TaskbarSpot.Put(handle.Handle, place);
        _placed = place;
        _placedAt = DateTime.UtcNow;
        if (_paintDue || _size != _paintedSize) Paint(); // Place follows every Draw that changed something, so the layered window gets each new look here
        // A child hides with the taskbar; an owned window (Windows refused the child) hides itself while a full-screen app is on the
        // taskbar's monitor, looked at on every read (no poll of its own).
        var hide = _owned && TaskbarSpot.FullScreenOn(place.Taskbar);
        if (hide == _hidden) return;
        _hidden = hide;
        TaskbarSpot.Show(handle.Handle, !hide);
    }

    /// Gives the layered window its picture (<see cref="TaskbarSpot.Paint"/>): software rendering cannot draw a child of Windows 11's taskbar.
    private void Paint()
    {
        if (_window is not { } window || window.TryGetPlatformHandle() is not { } handle) return;
        (_paintDue, _paintedSize) = (false, _size);
        _picture.Paint(handle.Handle, window.DrawInto, Palette.HitTest.A);
    }

    /// The right-click menu: Layout (the favourite layouts), Show (every reading, ticked), Position (right or left) and Font (the default and the fonts
    /// picked before) as sub-menus, the gadget's settings, Network traffic control (while it runs) and Hide.
    private IReadOnlyList<MenuRowItem> MenuRows() => new[]
    {
        MenuRowItem.Sub("Layout", "layout-dashboard", LayoutRows, _layouts.CurrentName),
        MenuRowItem.Sub("Show", "eye", ShowRows),
        MenuRowItem.Sub("Position", "arrows-left-right", PositionRows, GadgetSettings.PositionNames[(int)Look.Position]),
        MenuRowItem.Sub("Font", "cursor-text", FontRows, GadgetSettings.FontName(Look.Font)),
        Link("Gadget settings…", "settings", _windows.OpenGadgetSettings, ruleAbove: true),
        _windows.HasTraffic ? Link("Network traffic control…", "network", _windows.OpenApps) : null,
        new MenuRowItem("Hide gadget", string.Empty, false, string.Empty, new RelayCommand(() => SetShown(false)), RuleAbove: true, IsMuted: true, Icon: "eye-off"),
    }.OfType<MenuRowItem>().ToList();

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

    /// Right and Left, the one in force chosen.
    private IReadOnlyList<MenuRowItem> PositionRows() => Enum.GetValues<GadgetPosition>().Select(p => new MenuRowItem(GadgetSettings.PositionNames[(int)p], string.Empty,
        p == Look.Position, string.Empty, new RelayCommand(() => SetPosition(p)))).ToList();

    /// The default font and the fonts picked before, each in its own typeface, the one in force chosen; then the way to every installed font.
    private IReadOnlyList<MenuRowItem> FontRows()
    {
        var rows = GadgetFontCard.Rows(Look.RecentFonts.Prepend(string.Empty), Look.Font, SetFont).ToList();
        rows.Add(new MenuRowItem("More fonts…", string.Empty, false, string.Empty, new RelayCommand(_windows.OpenGadgetSettings), RuleAbove: true, IsMuted: true));
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
        _monitor.Updated -= Redraw;
        _monitor.ReadingsChanged -= AddRows;
        _weather.Updated -= Redraw;
        _settings.Changed -= Follow;
        _preview.Changed -= NeedReadings;
        _disposed = true;
        CloseWindow();
    }
}
