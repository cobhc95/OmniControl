using Avalonia.Media;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Osd;

public sealed class OsdPageViewModel : PageViewModel
{
    public override string Title => "OSD";
    public override string Icon => "M3,5 H21 A1,1 0 0 1 22,6 V18 A1,1 0 0 1 21,19 H3 A1,1 0 0 1 2,18 V6 A1,1 0 0 1 3,5 Z M4,7 V17 H20 V7 Z M6,13 H14 V15 H6 Z";
    public override string Keywords => "osd on-screen display overlay popup position style color colour accent opacity volume brightness windows";

    /// <summary>Every setting as a card: master switch, position, look, colours, opacity, one switch per element, Windows' own OSD.</summary>
    public IReadOnlyList<object> Primary { get; }
    public ItemGroup Customize { get; }
    public ItemGroup Elements { get; }

    /// <summary>Every setting in page order.</summary>
    public IReadOnlyList<object> Items { get; }

    public override IEnumerable<SearchEntry> SearchEntries =>
        Items.Select(i => new SearchEntry { Title = "OSD", Category = "OSD", Editor = i, Keywords = "on-screen display overlay popup" });

    public OsdPageViewModel(OsdService osd, HotkeyRegistry hotkeys)
    {
        var show = new ToggleItem("Show OSD", osd.Settings.Enabled, osd.SetEnabled) { Half = true };
        var windows = new ToggleItem("Hide Windows' own OSD", osd.Settings.HideWindowsOsd, osd.SetHideWindowsOsd) { Half = true };

        var position = new ChoiceItem("Position",
            Enum.GetValues<OsdPosition>().Select(OsdSettings.Describe).ToList(),
            OsdSettings.Describe(osd.Settings.Position),
            name =>
            {
                osd.SetPosition(Enum.GetValues<OsdPosition>().First(p => OsdSettings.Describe(p) == name));
                osd.Preview();
            });

        var styles = Enum.GetValues<OsdStyle>();
        var style = new GalleryItem("Style", styles.Select(OsdSettings.Describe).ToList(), Array.IndexOf(styles, osd.Settings.Style),
            () => OsdView.Build(osd.Settings, "Brightness", 60), i => osd.SetStyle(styles[i]));

        // Accent: "Match program" first, then the same presets the whole program offers.
        var accentNames = new List<string> { OsdSettings.MatchProgram };
        accentNames.AddRange(AccentService.Names);
        accentNames.AddRange(OsdPalette.ExtraAccents.Select(a => a.Name));
        var accent = new GalleryItem("Accent color", accentNames, Math.Max(0, accentNames.IndexOf(osd.Settings.Accent)),
            () => Swatches.Build(accentNames.Select(OsdSettings.ResolveAccent).ToList(),
                Math.Max(0, accentNames.IndexOf(osd.Settings.Accent))),
            i => osd.SetAccent(accentNames[i]));

        var backgrounds = OsdPalette.Backgrounds;
        var background = new GalleryItem("Background", backgrounds.Select(b => b.Name).ToList(),
            Math.Max(0, backgrounds.ToList().FindIndex(b => b.Hex == osd.Settings.Background)),
            () => Swatches.Build(backgrounds.Select(b => Color.Parse(b.Hex)).ToList(),
                Math.Max(0, backgrounds.ToList().FindIndex(b => b.Hex == osd.Settings.Background))),
            i => osd.SetBackground(backgrounds[i].Hex));

        // Previews that depend on other settings (the OSD sample, the match-program swatch) redraw when anything changes.
        osd.SettingsChanged += () => { style.Refresh(); accent.Refresh(); background.Refresh(); };

        var opacity = new SliderItem("Opacity", osd.Settings.Opacity, v => { osd.SetOpacity(v); return OperationResult.Ok("OSD opacity"); }, _ => { });

        // The page: the master switch and Windows' own OSD, then the collapsible "Customize OSD" and "Show in OSD" groups.
        Primary = new object[] { show, windows };
        Customize = new ItemGroup("OSD/Customize", "Customize OSD", new object[] { position, style, accent, background, opacity });
        Elements = new ItemGroup("OSD/Elements", "Show in OSD",
            OsdService.Elements.Select(e => (object)new ToggleItem($"Show {e}", osd.IsShown(e), on => osd.SetShown(e, on))).ToList());

        // Everything, in page order (used by search).
        Items = Primary.Concat(Customize.Items).Concat(Elements.Items).ToList();

        hotkeys.Register(new HotkeyAction("osd.toggle", "Toggle OSD", "OSD", "osd on-screen display overlay popup",
            _ => show.IsOn = !show.IsOn));
        hotkeys.Register(new HotkeyAction("osd.style.next", "Next OSD style", "OSD", "osd on-screen display style look",
            _ => style.Step(1)));
        hotkeys.Register(new HotkeyAction("osd.windows.toggle", "Toggle hiding Windows' own OSD", "OSD", "osd windows volume brightness popup",
            _ => windows.IsOn = !windows.IsOn));
    }
}
