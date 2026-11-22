using Avalonia.Controls.Templates;

namespace OmniControl.Screenshots;

/// Every screenshot, in sidebar order (pickers and the tray panel after the pages: they list what the pages registered). Not drawn, because
/// their view-models start Windows-facing work when made: the Volume page's own view-model, Easy view and FX window, Other PCs and its
/// pairing panel, the screenshot editor (MAP.md, open items).
internal static partial class Shots
{
    public static IEnumerable<(string Name, Action Shoot)> All(Rig rig, Camera camera) => new (string, Action)[]
    {
        ("shell templates", () => Use(ViewTemplate.For<SuggestedKeysSwitch, SuggestedKeysView>())),
        ("sample volume states", () => SampleVolumeStates(rig)),
        ("displays", () => Displays(rig, camera)),
        ("volume", () => VolumeMixer(camera)),
        ("smart-devices", () => Devices(rig, camera)),
        ("mouse", () => Mouse(rig, camera)),
        ("automation", () => Automation(rig, camera)),
        ("osd", () => OsdPage(rig, camera)),
        ("osd-styles", () => OsdStyles(camera)),
        ("resources-readings", () => ResourcesReadings(rig, camera)),
        ("resources-gadget-tab", () => ResourcesGadgetTab(rig, camera)),
        ("taskbar-gadget", () => TaskbarGadgets(rig, camera)),
        ("network-traffic-control", () => NetworkTraffic(rig, camera)),
        ("app-settings", () => AppSettingsPage(rig, camera)),
        ("actions-hotkeys", () => ActionsWindow(rig, camera)),
        ("action-picker", () => ActionPicker(rig, camera)),
        ("tray-panel", () => TrayPanel(rig, camera)),
        ("displays-light", () => Light(rig.Pages[0], camera, "displays-light")),
    };

    /// A page again in the light theme (the app's own theme switch: the theme variant; the surfaces and accent shades follow it).
    private static void Light(PageViewModel page, Camera camera, string name)
    {
        var was = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        camera.Page(page, name);
        Application.Current.RequestedThemeVariant = was;
    }

    /// Adds the feature's view templates as its module would (the modules themselves are not made: they start Windows-facing services).
    private static void Use(params IDataTemplate[] templates) => Application.Current!.DataTemplates.AddRange(templates);
}
