using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>Builds the OSD visual for a style and colours. Used by the pop-up and by the preview in the settings card.</summary>
public static class OsdView
{
    private const string Sun = "M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 Z M11,1 H13 V4 H11 Z M11,20 H13 V23 H11 Z M1,11 H4 V13 H1 Z M20,11 H23 V13 H20 Z";
    private const string Speaker = "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8 Q18,12 15,16 L16.5,17.5 Q21,12 16.5,6.5 Z";
    private const string Disc = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z";

    private static string IconFor(string label) =>
        label.StartsWith("Volume") ? Speaker : label.StartsWith("Contrast") ? Disc : Sun;

    public static Control Build(OsdSettings s, string label, int value)
    {
        value = Math.Clamp(value, 0, 100);
        var bg = Color.Parse(s.Background);
        var accent = s.ResolveAccent();
        var fg = ColorMath.Luminance(bg) > 0.55 ? Color.Parse("#1A1A1A") : Colors.White;
        var muted = ColorMath.Mix(bg, fg, 0.65);
        var track = ColorMath.Mix(bg, fg, 0.22);
        var edge = ColorMath.Mix(bg, fg, 0.16);

        Control Frame(Control child, double? w, double? h, double radius, Thickness pad) => new Border
        {
            Width = w ?? double.NaN,
            Height = h ?? double.NaN,
            Padding = pad,
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(edge),
            BorderThickness = new Thickness(1),
            Child = child,
        };

        TextBlock Text(string t, double size, Color c, bool bold = false) => new()
        {
            Text = t, FontSize = size, Foreground = new SolidColorBrush(c),
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        PathIcon Icon(double size) => new()
        {
            Data = Geometry.Parse(IconFor(label)), Width = size, Height = size,
            Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center,
        };

        switch (s.Style)
        {
            case OsdStyle.Pill:
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Width = 190 };
                row.Children.Add(Icon(16));
                var bar = Bar(value, 5, accent, track, false);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                var num = Text(value.ToString(), 13, fg);
                Grid.SetColumn(num, 2);
                row.Children.Add(num);
                return Frame(row, null, null, 20, new Thickness(14, 9));
            }
            case OsdStyle.BigNumber:
            {
                var col = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                col.Children.Add(Text(value.ToString(), 44, fg, true));
                var bar = Bar(value, 4, accent, track, false);
                bar.Width = 84;
                col.Children.Add(bar);
                col.Children.Add(Text(label, 11, muted));
                return Frame(col, 130, null, 14, new Thickness(0, 12, 0, 12));
            }
            case OsdStyle.Ring:
            {
                var g = new Grid { Width = 96, Height = 96 };
                g.Children.Add(new Ellipse
                {
                    Width = 76, Height = 76, Stroke = new SolidColorBrush(track), StrokeThickness = 7,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
                if (value > 0)
                {
                    var angle = Math.Min(value * 3.6, 359.9);
                    var rad = (angle - 90) * Math.PI / 180;
                    const double r = 38, c = 48;
                    var arc = new PathFigure
                    {
                        StartPoint = new Point(c, c - r), IsClosed = false,
                        Segments = new PathSegments
                        {
                            new ArcSegment
                            {
                                Point = new Point(c + r * Math.Cos(rad), c + r * Math.Sin(rad)),
                                Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = angle > 180,
                            },
                        },
                    };
                    g.Children.Add(new Avalonia.Controls.Shapes.Path
                    {
                        Data = new PathGeometry { Figures = new PathFigures { arc } },
                        Stroke = new SolidColorBrush(accent), StrokeThickness = 7, StrokeLineCap = PenLineCap.Round,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    });
                }
                g.Children.Add(Text(value.ToString(), 20, fg, true));
                return Frame(g, 100, 100, 50, new Thickness(0));
            }
            case OsdStyle.Vertical:
            {
                var col = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8, Height = 130 };
                col.Children.Add(Text(value.ToString(), 13, fg));
                var bar = Bar(value, 6, accent, track, true);
                bar.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(bar, 1);
                col.Children.Add(bar);
                var icon = Icon(16);
                Grid.SetRow(icon, 2);
                col.Children.Add(icon);
                return Frame(col, 52, null, 14, new Thickness(0, 12));
            }
            default:
            {
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
                var name = Text(label, 13, fg, true);
                name.HorizontalAlignment = HorizontalAlignment.Left;
                header.Children.Add(name);
                var num = Text(value.ToString(), 13, muted);
                Grid.SetColumn(num, 1);
                header.Children.Add(num);
                return Frame(new StackPanel { Children = { header, Bar(value, 6, accent, track, false) } }, 240, null, 12, new Thickness(16, 12));
            }
        }
    }

    private static Control Bar(int value, double thickness, Color fill, Color track, bool vertical)
    {
        var on = Math.Max(value, 0.01);
        var off = Math.Max(100 - value, 0.01);
        var grid = new Grid();
        var fillBar = new Border { Background = new SolidColorBrush(fill) };
        if (vertical)
        {
            grid.RowDefinitions.Add(new RowDefinition(off, GridUnitType.Star));
            grid.RowDefinitions.Add(new RowDefinition(on, GridUnitType.Star));
            Grid.SetRow(fillBar, 1);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(on, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(off, GridUnitType.Star));
        }
        grid.Children.Add(fillBar);
        return new Border
        {
            Background = new SolidColorBrush(track),
            CornerRadius = new CornerRadius(thickness / 2),
            ClipToBounds = true,
            Height = vertical ? double.NaN : thickness,
            Width = vertical ? thickness : double.NaN,
            Child = grid,
        };
    }
}
