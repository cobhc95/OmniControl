using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Displays.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Displays page over two sample monitors (<see cref="FakeEngine"/>); the software picture's ramps are fakes and the desktop colour
    /// effect stays neutral, so nothing is written to a display.
    private static void Displays(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<DisplaysPageViewModel, DisplaysPage>());
        var software = new SoftwareDisplayService(_ => new FakeGamma(), keepRecovery: false);
        var service = new DisplaysService(new FakeEngine(), rig.Actions, set => new SuggestedKeysSwitch(rig.Actions, set), rig.Osd, software, Rig.Report,
            rig.Presets, Rig.OpenPresets, rig.States, rig.Hardware, DisplaysPageViewModel.Info.Title);
        var page = new DisplaysPageViewModel(service);
        rig.Pages.Add(page);
        rig.Restore(page, service.NewRestorable(DisplaysPageViewModel.Info.RestoreKey!));
        Camera.Pump(1500); // each monitor and its orientation are read on workers
        page.Software.IsExpanded = false;
        for (var i = 0; i < page.Hardware.Subgroups.Count; i++) page.Hardware.Subgroups[i].IsExpanded = i == 0;
        camera.Page(page, "displays");
    }
}
