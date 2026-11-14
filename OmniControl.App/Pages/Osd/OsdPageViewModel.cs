using Avalonia.Media;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Osd;

public sealed class OsdPageViewModel : PageViewModel
{
    private readonly OsdService _osd;

    public override string Title => "OSD";

    public override string KeyHelp => "Enter on Position opens its list.";
    public override string Icon => "M3,5 H21 A1,1 0 0 1 22,6 V18 A1,1 0 0 1 21,19 H3 A1,1 0 0 1 2,18 V6 A1,1 0 0 1 3,5 Z M4,7 V17 H20 V7 Z M6,13 H14 V15 H6 Z";
    public override string Keywords => "osd on-screen display overlay popup position style color colour accent opacity volume brightness windows";

    /// <summary>Every setting as a card: master switch, position, look, colours, opacity, one switch per element, Windows' own OSD.</summary>
    public IReadOnlyList<object> Primary { get; }
    public ItemGroup Customize { get; }

    /// <summary>"Show in OSD": one switch per element the features declared (<see cref="OsdService.Declare"/>); rebuilt when one is added.</summary>
    public ItemGroup Elements { get => _elements; private set => SetProperty(ref _elements, value); }
    private ItemGroup _elements;

    /// <summary>Every setting in page order.</summary>
    private IEnumerable<object> Items => Primary.Concat(Customize.Items).Concat(Elements.Items);

    public override IEnumerable<SearchEntry> SearchEntries =>
        Items.Select(i => Entry(i, Title, "on-screen display overlay popup"));

    public OsdPageViewModel(OsdService osd, HotkeyRegistry hotkeys)
    {
        _osd = osd;
        var show = new ToggleItem("Show OSD", osd.Settings.Enabled, osd.SetEnabled) { Half = true };
        var windows = new ToggleItem("Hide Windows' own OSD", osd.Settings.HideWindowsOsd, osd.SetHideWindowsOsd)
        {
            Half = true,
            Tip = "Best-effort: Windows has no setting for it.",
        };

        var position = new ChoiceItem("Position",
            Enum.GetValues<OsdPosition>().Select(OsdSettings.Describe).ToList(),
            OsdSettings.Describe(osd.Settings.Position),
            name =>
            {
                osd.SetPosition(Enum.GetValues<OsdPosition>().First(p => OsdSettings.Describe(p) == name));
                osd.Preview();
            }) { Half = true };

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
        osd.SettingsChanged += () => { style.Refresh(); accent.Refresh(); background.Refresh(); };

        var opacity = new SliderItem("Opacity", osd.Settings.Opacity, v => { osd.SetOpacity(v); return OperationResult.Ok("OSD opacity"); }, _ => { }) { Half = true };

        // The page: the master switch and Windows' own OSD, then the collapsible "Customize OSD" and "Show in OSD" groups.
        Primary = new object[] { show, windows };
        Customize = new ItemGroup("OSD/Customize", "Customize OSD", new object[] { style, accent, background, position, opacity });
        _elements = BuildElements();
        osd.ElementsChanged += () => Elements = BuildElements();

        hotkeys.Register(new HotkeyAction("osd.toggle", "Toggle OSD", Title, "osd on-screen display overlay popup",
            _ => show.IsOn = !show.IsOn));
        hotkeys.Register(new HotkeyAction("osd.style.next", "Next OSD style", Title, "osd on-screen display style look",
            _ => style.Step(1)));
        hotkeys.Register(new HotkeyAction("osd.windows.toggle", "Toggle hiding Windows' OSD", Title, "osd windows volume brightness popup",
            _ => windows.IsOn = !windows.IsOn));
    }

    private ItemGroup BuildElements() => new("OSD/Elements", "Show in OSD",
        _osd.Elements.Select(e => (object)new ToggleItem(e.Name, _osd.IsShown(e.Name), on => _osd.SetShown(e.Name, on)) { Half = true }).ToList());
}
