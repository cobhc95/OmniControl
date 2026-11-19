using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;

namespace OmniControl.App.Shared.Ui.Pages;

/// A row of colour dots for gallery cards; the selected one is larger and ringed. Each dot carries its index, so a click on it picks it (see <see cref="GalleryRow"/>).
/// The dots sit in a <see cref="SwatchStrip"/>, so a card too narrow for all of them shows as many whole dots as fit around the chosen one.
public static class Swatches
{
    /// The style class of a dot that can be clicked (it grows a little under the pointer).
    public const string Clickable = "swatch";

    public static Control Build(IReadOnlyList<Color> colors, int selected) => Build(colors.Select(c => (IBrush)new SolidColorBrush(c)).ToList(), selected);

    /// <param name="edged">Every dot has a thin edge, so a white or black dot shows on either theme.</param>
    /// <param name="marked">The dot that follows Windows (it carries the small Windows mark); -1: none.</param>
    public static Control Build(IReadOnlyList<IBrush> fills, int selected, bool edged = false, int marked = -1)
    {
        var row = Row(spacing: 6, selected);
        for (var i = 0; i < fills.Count; i++)
        {
            var on = i == selected;
            row.Children.Add(Dot(fills[i], i, size: on ? 22 : 16, ring: on ? 2 : edged ? 1 : 0, ringKey: on ? "BrushMuted" : edged ? "BrushHoverEdge" : null, mark: i == marked));
        }
        return row;
    }

    /// The centred strip the dots sit in.
    internal static SwatchStrip Row(double spacing, int selected) => new() { Spacing = spacing, Selected = selected, HorizontalAlignment = HorizontalAlignment.Center };

    /// One round, clickable dot carrying its index; <paramref name="ringKey"/> names the theme brush of its ring (none when null);
    /// <paramref name="mark"/> draws the Windows mark inside it, in the colour that reads on its fill.
    internal static Border Dot(IBrush fill, int index, double size, double ring, string? ringKey, bool mark = false)
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
        if (mark) dot.Child = Mark(fill, size);
        return dot;
    }

    private static Avalonia.Controls.Shapes.Path Mark(IBrush fill, double size)
    {
        var ink = AccentService.OnAccent(fill is ISolidColorBrush solid ? solid.Color : Colors.Black);
        var path = new Avalonia.Controls.Shapes.Path
        {
            Width = size * MarkShare,
            Height = size * MarkShare,
            Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(ink),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        path.Bind(Avalonia.Controls.Shapes.Path.DataProperty, path.GetResourceObservable("WindowsMarkIcon"));
        return path;
    }

    private const double MarkShare = 0.45; // the mark's size as a share of its dot
}

/// The colour dots of an accent gallery (App settings' Accent colour, the OSD's): the names of <see cref="AccentService.Names"/> (and any extra
/// choices) drawn edged, so White shows on the light theme, with the Windows mark on "Windows accent".
public static class AccentSwatches
{
    public static Control Build(IReadOnlyList<string> names, Func<string, Color> resolve, int selected) =>
        Swatches.Build(names.Select(n => (IBrush)new SolidColorBrush(resolve(n))).ToList(), selected, edged: true, marked: IndexOf(names, AccentService.WindowsName));

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++) if (names[i] == name) return i;
        return -1;
    }
}

/// The one row of a gallery's dots. It never clips a dot: it takes only the width its card gives it and shows as many WHOLE dots as fit,
/// a run around the chosen one (the arrows and the wheel move the choice, so the run follows); the dots left out are not drawn and take no clicks.
public sealed class SwatchStrip : Panel
{
    public double Spacing { get; init; }

    /// The chosen dot's index (it is always shown); -1: none, the run starts at the first.
    public int Selected { get; init; }

    private (int First, int Last) _shown;

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        _shown = Fit(availableSize.Width);
        return new Size(RunWidth(_shown), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _shown = Fit(finalSize.Width);
        var x = Math.Max(0, (finalSize.Width - RunWidth(_shown)) / 2);
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var shown = i >= _shown.First && i <= _shown.Last;
            child.Opacity = shown ? 1 : 0;
            child.IsHitTestVisible = shown;
            var size = child.DesiredSize;
            child.Arrange(new Rect(shown ? x : 0, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            if (shown) x += size.Width + Spacing;
        }
        return finalSize;
    }

    /// The widest run of whole dots around the chosen one that fits <paramref name="width"/>, growing one dot to the right, then one to the left.
    private (int First, int Last) Fit(double width)
    {
        if (Children.Count == 0) return (0, -1);
        var start = Math.Clamp(Selected, 0, Children.Count - 1);
        int first = start, last = start;
        var used = Children[start].DesiredSize.Width;
        for (var grew = true; grew;)
        {
            grew = false;
            if (last + 1 < Children.Count && used + Spacing + Children[last + 1].DesiredSize.Width <= width + Fudge) { used += Spacing + Children[++last].DesiredSize.Width; grew = true; }
            if (first > 0 && used + Spacing + Children[first - 1].DesiredSize.Width <= width + Fudge) { used += Spacing + Children[--first].DesiredSize.Width; grew = true; }
        }
        return (first, last);
    }

    private double RunWidth((int First, int Last) run)
    {
        double width = 0;
        for (var i = run.First; i <= run.Last; i++) width += Children[i].DesiredSize.Width + (i > run.First ? Spacing : 0);
        return width;
    }

    private const double Fudge = 0.5; // layout rounding: a run that fits to half a pixel fits
}

/// Dots for the Theme gallery card: System (half dark, half light), Light and Dark; the selected one is larger and ringed.
public static class ThemeSwatches
{
    public static Control Build(int selected)
    {
        // Each dot shows its own theme whatever the current one is, so the sidebar colour is read from that theme's dictionary.
        Color dark = Sidebar(ThemeVariant.Dark), light = Sidebar(ThemeVariant.Light);
        var row = Swatches.Row(spacing: 8, selected);
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
