using Avalonia.Input;
using Avalonia.Layout;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// The Orientation card's picture: all four orientations side by side, each a small screen with a bar on the edge that is the top of
/// the picture, the current one ringed. Every screen carries its index and the clickable class, so a click picks it before the display
/// turns (a wrong turn is hard to read back, so the choice is visible up front).
internal static class OrientationPreview
{
    private const double Long = 26, Short = 17, Bar = 3, Edge = 1.5, SelectedEdge = 2.5, Spacing = 10, HoverRoom = 4;
    private const string OutlineKey = "BrushMuted", SelectedKey = "BrushAccent";

    public static Control Build(int selected)
    {
        var row = new GalleryStrip { Spacing = Spacing, Selected = selected };
        row.Margin = new Thickness(HoverRoom); // the hovered screen grows (Border.gallerypick), so it needs room to stay unclipped
        foreach (var orientation in Enum.GetValues<DisplayOrientation>())
            row.Children.Add(Screen(orientation, (int)orientation == selected));
        return row;
    }

    private static Border Screen(DisplayOrientation orientation, bool on)
    {
        var portrait = orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
        var key = on ? SelectedKey : OutlineKey;
        var screen = new Border
        {
            Width = portrait ? Short : Long,
            Height = portrait ? Long : Short,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(on ? SelectedEdge : Edge),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, // the whole screen, not just its outline, takes the click
            Tag = (int)orientation,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = TopEdge(orientation, key),
        };
        screen.Classes.Add(GalleryStrip.Clickable);
        screen.Bind(Border.BorderBrushProperty, screen.GetResourceObservable(key));
        ToolTip.SetTip(screen, DisplayOrientationCard.Title(orientation));
        return screen;
    }

    private static Border TopEdge(DisplayOrientation orientation, string key)
    {
        var horizontal = orientation is DisplayOrientation.Landscape or DisplayOrientation.LandscapeFlipped;
        var bar = new Border { Width = horizontal ? Long / 2 : Bar, Height = horizontal ? Bar : Long / 2, IsHitTestVisible = false };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(key));
        bar.HorizontalAlignment = orientation switch
        {
            DisplayOrientation.Portrait => HorizontalAlignment.Right,
            DisplayOrientation.PortraitFlipped => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Center,
        };
        bar.VerticalAlignment = orientation switch
        {
            DisplayOrientation.Landscape => VerticalAlignment.Top,
            DisplayOrientation.LandscapeFlipped => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        return bar;
    }
}
