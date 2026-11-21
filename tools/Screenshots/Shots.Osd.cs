using Avalonia.Layout;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.Osd.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The OSD page over the one OSD service (its list of pop-ups is what the other pages declared).
    private static void OsdPage(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<OsdPageViewModel, OsdPage>());
        var page = new OsdPageViewModel(rig.Osd, Rig.OpenPresets, _ => Task.FromResult(false),
            Array.Empty<RunningProgram>, Rig.Report);
        rig.Pages.Add(page);
        page.Customize.IsExpanded = true;
        camera.Page(page, "osd");
    }

    /// The volume pop-up in every style, drawn by the pop-up's own builder.
    private static void OsdStyles(Camera camera)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), ItemSpacing = 16, LineSpacing = 16, MaxWidth = 900 };
        foreach (var style in Enum.GetValues<OsdStyle>())
        {
            var settings = OsdSettings.Load();
            settings.Style = style;
            var view = OsdView.Build(settings, OsdService.VolumeElement, OsdLevelIcon.Speaker, 64);
            view.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(view);
        }
        camera.Fit(row, "osd-styles");
    }
}
