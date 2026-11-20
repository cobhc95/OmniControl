namespace OmniControl.App.Shared.Ui.Pages;

/// The mark on an accent choice's dot, the same in every accent gallery (App settings', the OSD's): the Windows mark on "Windows accent",
/// the hue ring around the user's own colour.
public static class AccentSwatch
{
    public static SwatchMark Mark(string name) => name switch
    {
        AccentService.WindowsName => SwatchMark.Windows,
        AccentService.CustomName => SwatchMark.Custom,
        _ => SwatchMark.None,
    };
}
