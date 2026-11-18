using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Pages.Restore;
using OmniControl.App.Presets;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Osd;

public sealed class OsdPageViewModel : PageViewModel
{
    private readonly OsdService _osd;
    private readonly IRestorable _restorable;
    private readonly Action<string?, string?> _openPresets;
    private readonly Func<OsdPlacementViewModel, Task<bool>> _askPlacement;
    private readonly Func<IReadOnlyList<RunningProgram>> _listedApps;
    private readonly SingleWindow<OsdDurationWindow> _durationWindow;

    public override string Title => "OSD";

    public override string? NavGroup => NavGroups.Workflow;

    public override IRestorable? Restorable => _restorable;

    public override string KeyHelp => "Enter on Position or Show on opens its menu.";
    public override string Icon => "M3,5 H21 A1,1 0 0 1 22,6 V18 A1,1 0 0 1 21,19 H3 A1,1 0 0 1 2,18 V6 A1,1 0 0 1 3,5 Z M4,7 V17 H20 V7 Z M6,13 H14 V15 H6 Z";
    public override string Keywords => "osd on-screen display overlay popup position place places custom screen screens monitor style color colour accent opacity duration time seconds how long exceptions volume brightness windows";

    /// <summary>Every setting as a card: master switch, position, look, colours, opacity, one switch per element, Windows' own OSD.</summary>
    public IReadOnlyList<object> Primary { get; }
    public ItemGroup Customize { get; }

    /// <summary>"Show in OSD": one switch per element the features declared (<see cref="OsdService.Declare"/>); rebuilt when one is added.</summary>
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

    /// <summary>Every setting in page order.</summary>
    private IEnumerable<object> Items => Primary.Concat(Customize.Items).Concat(Elements.Items);

    public override IEnumerable<SearchEntry> SearchEntries =>
        Items.Select(i => Entry(i, Title, i == _duration ? "on-screen display overlay popup duration time seconds how long per app event exceptions" : "on-screen display overlay popup"));

    private readonly SliderItem _duration;

    /// <param name="presets">The master list of preset groups: the page registers its named places.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group (the Position menu's "Manage places...").</param>
    /// <param name="askPlacement">Shows the placement window and answers whether OK was pressed.</param>
    /// <param name="listedApps">The programs of the app lists, offered by the OSD duration window's exceptions.</param>
    public OsdPageViewModel(OsdService osd, HotkeyRegistry hotkeys, PresetGroups presets, Action<string?, string?> openPresets,
        Func<OsdPlacementViewModel, Task<bool>> askPlacement, Func<IReadOnlyList<RunningProgram>> listedApps, Action<string> report)
    {
        (_osd, _openPresets, _askPlacement, _listedApps) = (osd, openPresets, askPlacement, listedApps);
        _durationWindow = new SingleWindow<OsdDurationWindow>(report);
        var show = new ToggleItem("Show OSD", osd.Settings.Enabled, osd.SetEnabled) { Half = true };
        var windows = new ToggleItem("Hide Windows' own OSD", osd.Settings.HideWindowsOsd, osd.SetHideWindowsOsd)
        {
            Half = true,
            Tip = "Best-effort: Windows has no setting for it.",
        };

        var position = new MenuChoiceItem("Position", osd.Settings.CurrentPlaceName, PositionRows) { Half = true };
        var showOn = new MenuChoiceItem("Show on", () => OsdScreens.Describe(osd.AllScreens, osd.Settings), ShowOnRows) { Half = true };

        var styles = Enum.GetValues<OsdStyle>();
        var style = new GalleryItem("Style", styles.Select(OsdSettings.Describe).ToList(), Array.IndexOf(styles, osd.Settings.Style),
            () => OsdView.Build(osd.Settings, OsdService.BrightnessElement, OsdLevelIcon.Sun, 60), i => osd.SetStyle(styles[i]));

        // Accent: "Match program" first, then the same presets the whole program offers.
        var accentNames = new List<string> { OsdSettings.MatchProgram };
        accentNames.AddRange(AccentService.Names);
        accentNames.AddRange(OsdPalette.ExtraAccents.Select(a => a.Name));
        int AccentIndex() => Math.Max(0, accentNames.IndexOf(osd.Settings.Accent));
        var accent = new GalleryItem("Accent color", accentNames, AccentIndex(),
            () => Swatches.Build(accentNames.Select(OsdSettings.ResolveAccent).ToList(), AccentIndex()),
            i => osd.SetAccent(accentNames[i]));

        var backgrounds = OsdPalette.Backgrounds;
        var backgroundHexes = backgrounds.Select(b => b.Hex).ToList();
        int BackgroundIndex() => Math.Max(0, backgroundHexes.IndexOf(osd.Settings.Background));
        var background = new GalleryItem("Background", backgrounds.Select(b => b.Name).ToList(), BackgroundIndex(),
            () => Swatches.Build(backgroundHexes.Select(Color.Parse).ToList(), BackgroundIndex()),
            i => osd.SetBackground(backgrounds[i].Hex));

        // Previews that depend on other settings (the OSD sample, the match-program swatch) redraw when anything changes.
        var opacity = new SliderItem("Opacity", osd.Settings.Opacity, v => { osd.SetOpacity(v); return OperationResult.Ok("OSD opacity"); }, _ => { }) { Half = true };
        // Duration counts in tenths of a second (one decimal); "Everything else" of the OSD duration window is the same setting.
        var duration = _duration = new SliderItem("Duration", Tenths(osd.Durations.EverythingElse), v => { osd.Durations.SetEverythingElse(v / Tenth); osd.Preview(); return OperationResult.Ok("OSD duration"); },
            _ => { }, Tenths(OsdDuration.MinSeconds), Tenths(OsdDuration.MaxSeconds), " s", Tenths(OsdDuration.Default))
        {
            Half = true,
            Decimals = OsdDuration.Decimals,
            Cog = new RelayCommand(OpenDuration),
            CogTip = "Duration per app or event…",
        };
        osd.SettingsChanged += () =>
        {
            style.Refresh(); accent.Refresh(); background.Refresh(); position.Refresh(); showOn.Refresh();
            duration.Load(Tenths(osd.Durations.EverythingElse)); // the OSD duration window changed it
        };
        // The settings were read from disk again (an import): the cards the service does not redraw itself show the new values without applying them.
        osd.Reloaded += () =>
        {
            show.Load(osd.Settings.Enabled);
            windows.Load(osd.Settings.HideWindowsOsd);
            opacity.Value = osd.Settings.Opacity;
            duration.Load(Tenths(osd.Durations.EverythingElse));
            style.Load(Math.Max(0, Array.IndexOf(styles, osd.Settings.Style)));
            accent.Load(AccentIndex());
            background.Load(BackgroundIndex());
        };

        // The page: the master switch and Windows' own OSD, then the collapsible "Customize OSD" and "Show in OSD" groups.
        Primary = new object[] { show, windows };
        Customize = new ItemGroup("OSD/Customize", "Customize OSD", new object[] { style, accent, background, position, showOn, opacity, duration });
        _elements = BuildElements();
        osd.ElementsChanged += () => Elements = BuildElements();

        // "Restore defaults": every setting goes through the cards, so the service and the page follow at once (the position through the service
        // alone, so no preview pop-up appears).
        _restorable = new SettingsRestore<OsdSettings>("osd", Title,
            () => "Turns Show OSD on, shows Windows' own OSD again, and puts the style, accent, background, position, the screens it shows on, opacity, duration and the Show in OSD switches back to their defaults. Your named places and duration exceptions are kept.",
            () => osd.Settings, () => new OsdSettings(),
            target =>
            {
                show.IsOn = target.Enabled;
                windows.IsOn = target.HideWindowsOsd;
                osd.SetPosition(target.Position);
                osd.SetShowOn(target.ShowOn, target.Screens);
                style.Index = Math.Max(0, Array.IndexOf(styles, target.Style));
                accent.Index = Math.Max(0, accentNames.IndexOf(target.Accent));
                background.Index = Math.Max(0, backgroundHexes.IndexOf(target.Background));
                opacity.Value = OsdSettings.ClampOpacity(target.Opacity);
                duration.Value = Tenths(target.Duration);
                foreach (var element in osd.Elements) osd.SetShown(element.Name, !target.Excluded.Contains(element.Name));
                Elements = BuildElements();
                return Task.FromResult(true);
            });

        // The named places are presets (the same provider pattern as the layers): the preset manager lists, renames and reorders them.
        presets.Register(new PresetGroup(OsdPlacePresets.GroupId, Title, "Places", new OsdPlacePresets(osd)));

        hotkeys.Register(new HotkeyAction("osd.toggle", "Toggle OSD", Title, "osd on-screen display overlay popup",
            _ => show.IsOn = !show.IsOn));
        hotkeys.Register(new HotkeyAction("osd.style.next", "Next OSD style", Title, "osd on-screen display style look",
            _ => style.Step(1)));
        hotkeys.Register(new HotkeyAction("osd.windows.toggle", "Toggle hiding Windows' OSD", Title, "osd windows volume brightness popup",
            _ => windows.IsOn = !windows.IsOn));
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
}
