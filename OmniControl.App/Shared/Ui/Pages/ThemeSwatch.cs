using Avalonia.Styling;

namespace OmniControl.App.Shared.Ui.Pages;

/// The dot of each Theme choice (System half dark and half light, Light, Dark), in the order of ThemeChoice. Each dot shows its own theme
/// whatever the current one is, so the sidebar colour is read from that theme's dictionary.
public static class ThemeSwatch
{
    public static IBrush Fill(int choice)
    {
        Color dark = Sidebar(ThemeVariant.Dark), light = Sidebar(ThemeVariant.Light);
        return choice switch
        {
            1 => new SolidColorBrush(light),
            2 => new SolidColorBrush(dark),
            _ => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(dark, 0), new GradientStop(dark, 0.5), new GradientStop(light, 0.5), new GradientStop(light, 1) },
            },
        };
    }

    private static Color Sidebar(ThemeVariant theme) =>
        Application.Current?.TryGetResource("BrushSidebar", theme, out var brush) == true && brush is ISolidColorBrush solid
            ? solid.Color
            : Colors.Transparent; // Theme.axaml defines BrushSidebar for both themes, so this is not expected
}
