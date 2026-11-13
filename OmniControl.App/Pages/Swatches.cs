using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace OmniControl.App.Pages;

/// <summary>A row of colour dots for gallery cards; the selected one is larger and ringed. Each dot carries its index, so a click on it picks it (see <see cref="GalleryRow"/>).</summary>
public static class Swatches
{
    /// <summary>The style class of a dot that can be clicked (it grows a little under the pointer).</summary>
    public const string Clickable = "swatch";

    public static Control Build(IReadOnlyList<Color> colors, int selected)
    {
        var row = Row(spacing: 6);
        for (var i = 0; i < colors.Count; i++)
        {
            var on = i == selected;
            row.Children.Add(Dot(new SolidColorBrush(colors[i]), i, size: on ? 22 : 16, ring: on ? 2 : 0, ringKey: on ? "BrushMuted" : null));
        }
        return row;
    }

    /// <summary>The centred row the dots sit in.</summary>
    internal static StackPanel Row(double spacing) =>
        new() { Orientation = Orientation.Horizontal, Spacing = spacing, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>One round, clickable dot carrying its index; <paramref name="ringKey"/> names the theme brush of its ring (none when null).</summary>
    internal static Border Dot(IBrush fill, int index, double size, double ring, string? ringKey)
    {
        var dot = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = fill,
            BorderThickness = new Thickness(ring),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = index,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        dot.Classes.Add(Clickable);
        if (ringKey is not null) dot.Bind(Border.BorderBrushProperty, dot.GetResourceObservable(ringKey));
        return dot;
    }
}

/// <summary>Dots for the Theme gallery card: System (half dark, half light), Light and Dark; the selected one is larger and ringed.</summary>
public static class ThemeSwatches
{
    public static Control Build(int selected)
    {
        // Each dot shows its own theme whatever the current one is, so the sidebar colour is read from that theme's dictionary.
        Color dark = Sidebar(ThemeVariant.Dark), light = Sidebar(ThemeVariant.Light);
        var row = Swatches.Row(spacing: 8);
        for (var i = 0; i < 3; i++) // order follows ThemeChoice: System, Light, Dark
        {
            var on = i == selected;
            IBrush fill = i switch
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
            row.Children.Add(Swatches.Dot(fill, i, size: on ? 26 : 20, ring: on ? 2 : 1, ringKey: on ? "BrushMuted" : "BrushHoverEdge"));
        }
        return row;
    }

    private static Color Sidebar(ThemeVariant theme) =>
        Application.Current?.TryGetResource("BrushSidebar", theme, out var brush) == true && brush is ISolidColorBrush solid
            ? solid.Color
            : Colors.Transparent; // Theme.axaml defines BrushSidebar for both themes, so this is not expected
}
