using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OmniControl.App.Pages;

/// <summary>A row of colour dots for gallery cards; the selected one is larger and ringed.</summary>
public static class Swatches
{
    public static Control Build(IReadOnlyList<Color> colors, int selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < colors.Count; i++)
        {
            var on = i == selected;
            var dot = new Border
            {
                Width = on ? 22 : 16,
                Height = on ? 22 : 16,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(colors[i]),
                BorderThickness = new Thickness(on ? 2 : 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (on) dot.Bind(Border.BorderBrushProperty, dot.GetResourceObservable("BrushMuted"));
            row.Children.Add(dot);
        }
        return row;
    }
}

/// <summary>Dots for the Theme gallery card: System (half dark, half light), Light and Dark; the selected one is larger and ringed.</summary>
public static class ThemeSwatches
{
    private static readonly Color Dark = Color.Parse("#1C1C1C"), Light = Color.Parse("#F3F3F3");

    public static Control Build(int selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 3; i++) // order follows ThemeChoice: System, Light, Dark
        {
            var on = i == selected;
            IBrush fill = i switch
            {
                1 => new SolidColorBrush(Light),
                2 => new SolidColorBrush(Dark),
                _ => new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Dark, 0), new GradientStop(Dark, 0.5), new GradientStop(Light, 0.5), new GradientStop(Light, 1) },
                },
            };
            var dot = new Border
            {
                Width = on ? 26 : 20,
                Height = on ? 26 : 20,
                CornerRadius = new CornerRadius(13),
                Background = fill,
                BorderThickness = new Thickness(on ? 2 : 1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.Bind(Border.BorderBrushProperty, dot.GetResourceObservable(on ? "BrushMuted" : "BrushHoverEdge"));
            row.Children.Add(dot);
        }
        return row;
    }
}
