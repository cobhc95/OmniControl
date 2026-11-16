using System.Globalization;
using OmniControl.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>
/// Builds the OSD visual for a style and colours. Used by the pop-up and by the preview in the settings card.
/// A built level visual changes its value in place without allocating or re-laying out the window: the number has a fixed width
/// (that of "100"), the bar and ring are scaled or swept rather than rebuilt, and the number texts are made once.
/// </summary>
public static class OsdView
{
    /// <summary>The value shown by samples (the position preview, the start-up drawing).</summary>
    public const int SampleValue = 50;

    private const string Sun = "M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 Z M11,1 H13 V4 H11 Z M11,20 H13 V23 H11 Z M1,11 H4 V13 H1 Z M20,11 H23 V13 H20 Z";
    private const string Speaker = "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8 Q18,12 15,16 L16.5,17.5 Q21,12 16.5,6.5 Z";
    private const string Disc = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z";
    private const string Thermometer = "M10,4 A2,2 0 0 1 14,4 V13.2 A4.5,4.5 0 1 1 10,13.2 Z";
    private const string Play = "M7,4 V20 L20,12 Z";
    private const string Pause = "M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z";
    private const string Note = "M9,3 V15.3 A3.5,3.5 0 1 0 11,18.5 V8 H17 V3 Z";
    private const string Plug = "M8,2 V7 H10 V2 Z M14,2 V7 H16 V2 Z M6,8 H18 V13 A6,6 0 0 1 14,18.7 V22 H10 V18.7 A6,6 0 0 1 6,13 Z";

    // Sizes in device-independent pixels, shared by the styles.
    private const double BodyText = 13, SubtitleText = 12, CaptionText = 11, RingText = 20, BigText = 44;
    private const double IconSize = 16, MessageIconSize = 22;
    private const double CardRadius = 12, TallRadius = 14, PillRadius = 20;
    private const double ClassicWidth = 240, PillRowWidth = 190, BigNumberWidth = 130, BigNumberBarWidth = 84, VerticalWidth = 52;
    private const double VerticalHeight = 130, MessageMaxWidth = 340, LineSpacing = 8;
    private const double RingBox = 96, RingFrame = 100, RingTrack = 76, RingStroke = 7, RingRadius = 38;
    private const double TopAngle = -90, FullTurn = 360;
    private static readonly Thickness CardPad = new(16, 12), TallPad = new(0, 12);

    // Colour mixes from the background towards the text colour, and the background brightness above which text turns dark.
    private const double LightBackground = 0.55, MutedMix = 0.65, TrackMix = 0.22, EdgeMix = 0.16;

    /// <summary>"0" to "100", made once so a value change allocates no text.</summary>
    private static readonly string[] Numbers =
        Enumerable.Range(0, Percent.Max + 1).Select(n => n.ToString(CultureInfo.CurrentCulture)).ToArray();

    // The level shown for a muted or zero volume: its icon and number turn red so the state reads at a glance.
    private static readonly SolidColorBrush Silent = new(Palette.Danger);

    public static Control Build(OsdSettings s, string label, OsdLevelIcon icon, int value) => Build(s, label, icon, value, out _);

    /// <summary>
    /// Builds the level pop-up and hands back <paramref name="setValue"/>, which changes the shown value in place. Re-using the built
    /// visual while values stream in (a wheel spin, a held key) skips building and laying out a new one for every step.
    /// </summary>
    public static Control Build(OsdSettings s, string label, OsdLevelIcon icon, int value, out Action<int> setValue)
    {
        var updates = new List<Action<int>>();
        var built = BuildLevel(s, label, icon, updates);
        var all = updates.ToArray();
        setValue = v =>
        {
            v = Percent.Clamp(v);
            foreach (var update in all) update(v);
        };
        setValue(value);
        return built;
    }

    private static Control BuildLevel(OsdSettings s, string label, OsdLevelIcon icon, List<Action<int>> updates)
    {
        var (bg, accent, fg, muted, track, edge) = Scheme.From(s);
        var isVolume = icon == OsdLevelIcon.Speaker;
        var iconPath = icon switch { OsdLevelIcon.Speaker => Speaker, OsdLevelIcon.Disc => Disc, OsdLevelIcon.Thermometer => Thermometer, _ => Sun };

        Control Frame(Control child, double? w, double? h, double radius, Thickness pad) => OsdView.Frame(bg, edge, child, w, h, radius, pad);

        // The number sits in a box as wide as "100" (an invisible copy sets the width), so a value with fewer digits never
        // resizes the box, moves the bar beside it or re-places the window.
        Control Number(double size, Color c, bool bold, HorizontalAlignment align)
        {
            var number = Text(string.Empty, size, c, bold);
            number.HorizontalAlignment = align;
            var widest = Text(Numbers[Percent.Max], size, c, bold);
            widest.Opacity = 0;
            var normal = number.Foreground;
            updates.Add(v =>
            {
                number.Text = Numbers[v];
                if (isVolume) number.Foreground = v == 0 ? Silent : normal;
            });
            return new Grid { Children = { widest, number }, VerticalAlignment = VerticalAlignment.Center };
        }

        Control Bar(double thickness, bool vertical) => OsdView.Bar(thickness, accent, track, vertical, updates);

        PathIcon Icon()
        {
            var pathIcon = new PathIcon
            {
                Data = Geometry.Parse(iconPath), Width = IconSize, Height = IconSize,
                Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center,
            };
            var normal = pathIcon.Foreground;
            if (isVolume) updates.Add(v => pathIcon.Foreground = v == 0 ? Silent : normal);
            return pathIcon;
        }

        switch (s.Style)
        {
            case OsdStyle.Pill:
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Width = PillRowWidth };
                row.Children.Add(Icon());
                var bar = Bar(5, false);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                var num = Number(BodyText, fg, false, HorizontalAlignment.Right);
                Grid.SetColumn(num, 2);
                row.Children.Add(num);
                return Frame(row, null, null, PillRadius, new Thickness(14, 9));
            }
            case OsdStyle.BigNumber:
            {
                var col = new StackPanel { Spacing = LineSpacing, HorizontalAlignment = HorizontalAlignment.Center };
                col.Children.Add(Number(BigText, fg, true, HorizontalAlignment.Center));
                var bar = Bar(4, false);
                bar.Width = BigNumberBarWidth;
                col.Children.Add(bar);
                col.Children.Add(Text(label, CaptionText, muted));
                return Frame(col, BigNumberWidth, null, TallRadius, TallPad);
            }
            case OsdStyle.Ring:
            {
                var g = new Grid { Width = RingBox, Height = RingBox };
                g.Children.Add(new Ellipse
                {
                    Width = RingTrack, Height = RingTrack, Stroke = new SolidColorBrush(track), StrokeThickness = RingStroke,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
                // The arc's box puts the middle of its stroke on a circle of RingRadius around the ring's centre.
                var arc = new Arc
                {
                    Width = 2 * RingRadius + RingStroke, Height = 2 * RingRadius + RingStroke, StartAngle = TopAngle,
                    Stroke = new SolidColorBrush(accent), StrokeThickness = RingStroke, StrokeLineCap = PenLineCap.Round,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                updates.Add(v => arc.SweepAngle = v * FullTurn / Percent.Max);
                g.Children.Add(arc);
                g.Children.Add(Number(RingText, fg, true, HorizontalAlignment.Center));
                return Frame(g, RingFrame, RingFrame, RingFrame / 2, new Thickness(0));
            }
            case OsdStyle.Vertical:
            {
                var col = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = LineSpacing, Height = VerticalHeight };
                col.Children.Add(Number(BodyText, fg, false, HorizontalAlignment.Center));
                var bar = Bar(6, true);
                bar.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(bar, 1);
                col.Children.Add(bar);
                var levelIcon = Icon();
                Grid.SetRow(levelIcon, 2);
                col.Children.Add(levelIcon);
                return Frame(col, VerticalWidth, null, TallRadius, TallPad);
            }
            default:
            {
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, LineSpacing) };
                var name = Text(label, BodyText, fg, true);
                name.HorizontalAlignment = HorizontalAlignment.Left;
                header.Children.Add(name);
                var num = Number(BodyText, muted, false, HorizontalAlignment.Right);
                Grid.SetColumn(num, 1);
                header.Children.Add(num);
                return Frame(new StackPanel { Children = { header, Bar(6, false) } }, ClassicWidth, null, CardRadius, CardPad);
            }
        }
    }

    /// <summary>A pop-up that tells something (icon, a title, an optional second line) instead of showing a level. Uses the chosen colours, not the level styles.</summary>
    public static Control BuildMessage(OsdSettings s, OsdMessage message)
    {
        var (bg, accent, fg, muted, _, edge) = Scheme.From(s);

        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Line(message.Title, BodyText, fg, true));
        if (!string.IsNullOrWhiteSpace(message.Subtitle)) lines.Children.Add(Line(message.Subtitle, SubtitleText, muted, false));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, MaxWidth = MessageMaxWidth };
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse(IconPath(message.Icon)), Width = MessageIconSize, Height = MessageIconSize,
            Foreground = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(lines, 1);
        row.Children.Add(lines);
        return Frame(bg, edge, row, null, null, CardRadius, CardPad);
    }

    private static TextBlock Line(string text, double size, Color color, bool bold)
    {
        var t = Text(text, size, color, bold);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static string IconPath(OsdIcon icon) => icon switch
    {
        OsdIcon.Play => Play,
        OsdIcon.Pause => Pause,
        OsdIcon.Track => Note,
        OsdIcon.Screenshot => Hotkeys.ActionSections.ScreenshotIcon,
        _ => Plug,
    };

    /// <summary>The colours every pop-up style is drawn with, worked out once from the settings.</summary>
    private sealed record Scheme(Color Bg, Color Accent, Color Fg, Color Muted, Color Track, Color Edge)
    {
        public static Scheme From(OsdSettings s)
        {
            var bg = Color.Parse(s.Background);
            var fg = ColorMath.Luminance(bg) > LightBackground ? Palette.Ink : Colors.White;
            return new Scheme(bg, s.ResolveAccent(), fg, ColorMath.Mix(bg, fg, MutedMix), ColorMath.Mix(bg, fg, TrackMix), ColorMath.Mix(bg, fg, EdgeMix));
        }
    }

    private static Control Frame(Color bg, Color edge, Control child, double? w, double? h, double radius, Thickness pad) => new Border
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

    private static TextBlock Text(string t, double size, Color c, bool bold = false) => new()
    {
        Text = t, FontSize = size, Foreground = new SolidColorBrush(c),
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A level bar; its fill follows the value through <paramref name="updates"/>. The fill is scaled from its start edge, which only
    /// changes how it is drawn: no layout pass per value.
    /// </summary>
    private static Control Bar(double thickness, Color fill, Color track, bool vertical, List<Action<int>> updates)
    {
        var scale = new ScaleTransform();
        var fillBar = new Border
        {
            Background = new SolidColorBrush(fill),
            RenderTransform = scale,
            RenderTransformOrigin = vertical ? new RelativePoint(0.5, 1, RelativeUnit.Relative) : new RelativePoint(0, 0.5, RelativeUnit.Relative),
        };
        if (vertical) updates.Add(v => scale.ScaleY = v / (double)Percent.Max);
        else updates.Add(v => scale.ScaleX = v / (double)Percent.Max);
        return new Border
        {
            Background = new SolidColorBrush(track),
            CornerRadius = new CornerRadius(thickness / 2),
            ClipToBounds = true,
            Height = vertical ? double.NaN : thickness,
            Width = vertical ? thickness : double.NaN,
            Child = fillBar,
        };
    }
}
