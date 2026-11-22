using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Osd.Pages;

public sealed class OsdPageViewModel : PageViewModel, IDisposable
{
    private readonly OsdService _osd;
    private readonly Action<string?, string?> _openPresets;
    private readonly Func<OsdPlacementViewModel, Task<bool>> _askPlacement;
    private readonly Func<IReadOnlyList<RunningProgram>> _listedApps;
    private readonly SingleWindow<OsdDurationWindow> _durationWindow;

    public static readonly PageInfo Info = new("OSD", "M3,5 H21 A1,1 0 0 1 22,6 V18 A1,1 0 0 1 21,19 H3 A1,1 0 0 1 2,18 V6 A1,1 0 0 1 3,5 Z M4,7 V17 H20 V7 Z M6,13 H14 V15 H6 Z", NavGroups.System,
        "osd on-screen display overlay popup position place places custom screen screens monitor style color colour accent opacity duration time seconds how long exceptions volume brightness windows",
        "osd");

    public override string KeyHelp => "Enter on Position or Show on opens its menu.";

    /// Every setting as a card: master switch, position, look, colours, opacity, one switch per element, Windows' own OSD.
    public IReadOnlyList<object> Primary { get; }
    public ItemGroup Customize { get; }

    /// "Show in OSD": one switch per element the features declared (<see cref="OsdService.Declare"/>); rebuilt when one is added.
    public ItemGroup Elements
    {
        get => _elements;
        private set
        {
            var old = _elements;
            if (SetProperty(ref _elements, value)) old?.Dispose();
        }
    }
    private ItemGroup _elements;

    /// Every setting in page order.
    private IEnumerable<object> Items => Primary.Concat(Customize.Items).Concat(Elements.Items);

    public override IEnumerable<SearchEntry> SearchEntries => Items.OfType<IEditorItem>().Select(i => Entry(i, Card(i.Label)));

    private const string ShowLabel = "Show OSD", WindowsLabel = "Hide Windows' own OSD", StyleLabel = "Style", AccentLabel = "Accent colour",
        BackgroundLabel = "Background", PositionLabel = "Position", ShowOnLabel = "Show on", OpacityLabel = "Opacity", DurationLabel = "Duration";

    /// The page's search words (<c>FeaturePage.Words</c>): its cards and the service's elements.
    public static IEnumerable<string> Words(OsdService? osd) =>
        new[] { ShowLabel, WindowsLabel, StyleLabel, AccentLabel, BackgroundLabel, PositionLabel, ShowOnLabel, OpacityLabel, DurationLabel }
            .Concat(osd?.Elements.Select(e => e.Name) ?? Array.Empty<string>()).Select(label => Card(label).Words);

    private static SearchCard Card(string label) => new(label, label == DurationLabel
        ? "on-screen display overlay popup duration time seconds how long per app event exceptions" : "on-screen display overlay popup", Title: Info.Title);

    /// <param name="openPresets">Opens the preset manager at a section and group (the Position menu's "Manage places...").</param>
    /// <param name="askPlacement">Shows the placement window and answers whether OK was pressed.</param>
    /// <param name="listedApps">The programs of the app lists, offered by the OSD duration window's exceptions.</param>
    public OsdPageViewModel(OsdService osd, Action<string?, string?> openPresets,
        Func<OsdPlacementViewModel, Task<bool>> askPlacement, Func<IReadOnlyList<RunningProgram>> listedApps, Action<string> report) : base(Info)
    {
        (_osd, _openPresets, _askPlacement, _listedApps) = (osd, openPresets, askPlacement, listedApps);
        _durationWindow = new SingleWindow<OsdDurationWindow>(report);
        var show = new ToggleItem(ShowLabel, osd.Settings.Enabled, osd.SetEnabled) { Half = true };
        var windows = new ToggleItem(WindowsLabel, osd.Settings.HideWindowsOsd, osd.SetHideWindowsOsd)
        {
            Half = true,
            Tip = "Best-effort: Windows has no setting for it.",
        };

        var position = new MenuChoiceItem(PositionLabel, osd.Settings.CurrentPlaceName, PositionRows) { Half = true };
        var showOn = new MenuChoiceItem(ShowOnLabel, () => OsdScreens.Describe(osd.AllScreens, osd.Settings), ShowOnRows) { Half = true };

        var styles = Enum.GetValues<OsdStyle>();
        var style = new GalleryItem(StyleLabel, styles.Select(OsdSettings.Describe).ToList(), Array.IndexOf(styles, osd.Settings.Style),
            () => OsdView.Build(osd.Settings, OsdService.BrightnessElement, OsdLevelIcon.Sun, 60), i => osd.SetStyle(styles[i]));

        // Accent: "Match program" first, then the same presets the whole program offers.
        var accentNames = new List<string> { OsdSettings.MatchProgram };
        accentNames.AddRange(AccentService.Names);
        accentNames.AddRange(AccentService.OsdExtras.Select(a => a.Name));
        int AccentIndex() => Math.Max(0, accentNames.IndexOf(osd.Settings.Accent));
        var accent = new GalleryItem(AccentLabel, accentNames, AccentIndex(),
            i => (new SolidColorBrush(OsdSettings.ResolveAccent(accentNames[i])), AccentSwatch.Mark(accentNames[i])),
            i => osd.SetAccent(accentNames[i]));

        var backgrounds = OsdPalette.Backgrounds;
        var backgroundHexes = backgrounds.Select(b => b.Hex).ToList();
        int BackgroundIndex() => Math.Max(0, backgroundHexes.IndexOf(osd.Settings.Background));
        var background = new GalleryItem(BackgroundLabel, backgrounds.Select(b => b.Name).ToList(), BackgroundIndex(),
            i => (new SolidColorBrush(Color.Parse(backgroundHexes[i])), SwatchMark.None),
            i => osd.SetBackground(backgrounds[i].Hex));

        // Previews that depend on other settings (the OSD sample, the match-program swatch) redraw when anything changes.
        var opacity = new SliderItem(OpacityLabel, osd.Settings.Opacity, v => { osd.SetOpacity(v); return OperationResult.Ok("OSD opacity"); }, _ => { }) { Half = true };
        // Duration counts in tenths of a second (one decimal); "Everything else" of the OSD duration window is the same setting.
        var duration = new SliderItem(DurationLabel, Tenths(osd.Durations.EverythingElse), v => { osd.Durations.SetEverythingElse(v / Tenth); osd.Preview(); return OperationResult.Ok("OSD duration"); },
            _ => { }, Tenths(OsdDuration.MinSeconds), Tenths(OsdDuration.MaxSeconds), " s", Tenths(OsdDuration.Default))
        {
            Half = true,
            Decimals = OsdDuration.Decimals,
            Cog = new RelayCommand(OpenDuration),
            CogTip = "Duration per app or event…",
        };
        int StyleIndex() => Math.Max(0, Array.IndexOf(styles, osd.Settings.Style));
        _followed.Follow<Action>(h => osd.SettingsChanged += h, h => osd.SettingsChanged -= h, () =>
        {
            style.Refresh(); accent.Refresh(); background.Refresh(); position.Refresh(); showOn.Refresh();
            duration.Load(Tenths(osd.Durations.EverythingElse)); // the OSD duration window changed it
            show.Load(osd.Settings.Enabled); // the module's actions changed these
            windows.Load(osd.Settings.HideWindowsOsd);
            style.Load(StyleIndex());
        });
        // The settings were read from disk again (an import): the cards the service does not redraw itself show the new values without applying them.
        _followed.Follow<Action>(h => osd.Reloaded += h, h => osd.Reloaded -= h, () =>
        {
            show.Load(osd.Settings.Enabled);
            windows.Load(osd.Settings.HideWindowsOsd);
            opacity.Value = osd.Settings.Opacity;
            duration.Load(Tenths(osd.Durations.EverythingElse));
            style.Load(StyleIndex());
            accent.Load(AccentIndex());
            background.Load(BackgroundIndex());
        });

        // The page: the master switch and Windows' own OSD, then the collapsible "Customize OSD" and "Show in OSD" groups.
        Primary = new object[] { show, windows };
        Customize = new ItemGroup("OSD/Customize", "Customize OSD", new object[] { style, accent, background, position, showOn, opacity, duration });
        _elements = BuildElements();
        _followed.Follow<Action>(h => osd.ElementsChanged += h, h => osd.ElementsChanged -= h, () => Elements = BuildElements());
    }

    // The Position menu: the seven built-in places, the user's named places, "Custom position..." and "Manage places...".
    private IReadOnlyList<MenuRowItem> PositionRows()
    {
        var settings = _osd.Settings;
        var rows = Enum.GetValues<OsdPosition>().Select(p => new MenuRowItem(OsdSettings.Describe(p), string.Empty,
            settings.CustomPlace is null && settings.Position == p, string.Empty, new RelayCommand(() => { _osd.SetPosition(p); _osd.Preview(); }))).ToList();
        for (var i = 0; i < _osd.Places.All.Count; i++)
        {
            var place = _osd.Places.All[i];
            if (i == 0) rows.Add(MenuRowItem.Header("Your places", ruleAbove: true));
            rows.Add(new MenuRowItem(place.Name, string.Empty, settings.CustomPlace == place.Id, string.Empty, new RelayCommand(() => { _osd.UsePlace(place.Id); _osd.Preview(); })));
        }
        rows.Add(new MenuRowItem("Custom position…", string.Empty, false, string.Empty, new AsyncRelayCommand(CustomPositionAsync), RuleAbove: true, IsAccent: true));
        rows.Add(new MenuRowItem("Manage places…", string.Empty, false, string.Empty, new RelayCommand(() => _openPresets(Title, OsdPlacePresets.GroupId)), IsMuted: true));
        return rows;
    }

    // The Show on menu: all screens, the screen with the mouse, then a ticked list of each screen (several can be ticked).
    private IReadOnlyList<MenuRowItem> ShowOnRows()
    {
        var settings = _osd.Settings;
        var rows = new List<MenuRowItem>
        {
            new("All screens", string.Empty, settings.ShowOn == OsdShowOn.AllScreens, string.Empty, new RelayCommand(() => ShowOn(OsdShowOn.AllScreens))),
            new("Screen with the mouse", string.Empty, settings.ShowOn == OsdShowOn.MouseScreen, string.Empty, new RelayCommand(() => ShowOn(OsdShowOn.MouseScreen))),
        };
        var screens = _osd.AllScreens;
        if (screens.Count > 1) rows.Add(MenuRowItem.Header("Screens", ruleAbove: true));
        foreach (var screen in screens.Where(_ => screens.Count > 1))
        {
            var key = OsdScreens.KeyOf(screen);
            rows.Add(new MenuRowItem(OsdScreens.NameOf(screens, screen), string.Empty, settings.ShowOn == OsdShowOn.Chosen && settings.Screens.Contains(key), string.Empty,
                new RelayCommand(() => { _osd.ToggleScreen(key); _osd.Preview(); }), KeepsOpen: true));
        }
        return rows;
    }

    private void ShowOn(OsdShowOn on)
    {
        _osd.SetShowOn(on);
        _osd.Preview();
    }

    private async Task CustomPositionAsync()
    {
        await Guard.RunAsync("OSD placement window", async () =>
        {
            using var model = new OsdPlacementViewModel(_osd);
            if (await _askPlacement(model)) model.Save();
        });
    }

    // The Duration card's slider counts in steps of the duration's last decimal (tenths of a second).
    private static readonly double Tenth = Math.Pow(10, OsdDuration.Decimals);

    private static int Tenths(double seconds) => (int)Math.Round(seconds * Tenth);

    // The cog of the Duration card: the "OSD duration" window (one at a time).
    private void OpenDuration() => _durationWindow.Show(() => new OsdDurationWindow { DataContext = new OsdDurationViewModel(_osd, _listedApps) },
        closed => (closed.DataContext as IDisposable)?.Dispose(), "Couldn't open the OSD duration window");

    private ItemGroup BuildElements() => new("OSD/Elements", "Show in OSD",
        _osd.Elements.Select(e => (object)new ToggleItem(e.Name, _osd.IsShown(e.Name), on => _osd.SetShown(e.Name, on)) { Half = true }).ToList());

    private readonly Subscriptions _followed = new(); // the OSD service's events this page follows, given back at Dispose

    public void Dispose() => _followed.Dispose();
}
