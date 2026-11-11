using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx;

/// <summary>
/// Frequency-response graph of the equaliser with one draggable node per band. Draws and handles input only:
/// all values live in <see cref="EqPanelItem"/> (its DataContext), which saves them.
/// Mouse only: drag a node, scroll to change Q. Keyboard control is the card's (see <c>ISideNavigable</c> and <c>RowEditing.TryNavigateInside</c>).
/// </summary>
public sealed class EqGraph : Control
{
    private const double Left = 38, Right = 10, Top = 10, Bottom = 22;
    private static readonly double[] GridFreqs = { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
    private static readonly double[] GridDb = { -12, -6, 0, 6, 12 };

    private EqPanelItem? _panel;
    private int _drag = -1, _hover = -1;

    public EqGraph()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_panel is not null) _panel.Redraw -= InvalidateVisual;
        _panel = DataContext as EqPanelItem;
        if (_panel is not null) _panel.Redraw += InvalidateVisual;
        InvalidateVisual();
    }

    // ---- geometry ----

    private double PlotW => Math.Max(1, Bounds.Width - Left - Right);
    private double PlotH => Math.Max(1, Bounds.Height - Top - Bottom);
    private double X(double freq) => Left + PlotW * Math.Log(freq / Eq.MinFreq) / Math.Log(Eq.MaxFreq / Eq.MinFreq);
    private double Y(double db) => Top + PlotH * (Eq.MaxGain - db) / (Eq.MaxGain - Eq.MinGain);
    private double FreqAt(double x) => Eq.MinFreq * Math.Pow(Eq.MaxFreq / Eq.MinFreq, Math.Clamp((x - Left) / PlotW, 0, 1));
    private double DbAt(double y) => Eq.MaxGain - (Eq.MaxGain - Eq.MinGain) * Math.Clamp((y - Top) / PlotH, 0, 1);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    private FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    // ---- drawing ----

    public override void Render(DrawingContext ctx)
    {
        var accent = Brush("BrushAccent", Brushes.DodgerBlue);
        var soft = Brush("BrushAccentSoft", Brushes.Transparent);
        var divider = Brush("BrushDivider", Brushes.Gray);
        var muted = Brush("BrushMuted", Brushes.Gray);
        var card = Brush("BrushBackground", Brushes.Black);
        var fine = new Pen(divider, 0.5);

        ctx.DrawRectangle(card, new Pen(divider, 1), new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1), 8, 8);

        foreach (var f in GridFreqs)
        {
            var x = X(f);
            ctx.DrawLine(fine, new Point(x, Top), new Point(x, Top + PlotH));
            var label = Text(f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}", 11, muted);
            ctx.DrawText(label, new Point(x - label.Width / 2, Top + PlotH + 4));
        }
        foreach (var db in GridDb)
        {
            var y = Y(db);
            ctx.DrawLine(db == 0 ? new Pen(muted, 1) : fine, new Point(Left, y), new Point(Left + PlotW, y));
            var label = Text(db > 0 ? $"+{db:0}" : $"{db:0}", 11, muted);
            ctx.DrawText(label, new Point(Left - 6 - label.Width, y - label.Height / 2));
        }

        if (_panel is null) return;
        var bands = _panel.Export();

        // The selected band's own contribution, shaded down to 0 dB.
        var own = new StreamGeometry();
        using (var g = own.Open())
        {
            var sel = bands[_panel.Selected.Index];
            g.BeginFigure(new Point(Left, Y(0)), true);
            for (var i = 0; i <= 160; i++)
            {
                var f = Eq.MinFreq * Math.Pow(Eq.MaxFreq / Eq.MinFreq, i / 160.0);
                g.LineTo(new Point(Left + PlotW * i / 160.0, Y(Math.Clamp(EqMath.ResponseDb(sel, f), -12, 12))));
            }
            g.LineTo(new Point(Left + PlotW, Y(0)));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(soft, null, own);

        // The combined response of all bands.
        var total = new StreamGeometry();
        using (var g = total.Open())
        {
            for (var i = 0; i <= 240; i++)
            {
                var f = Eq.MinFreq * Math.Pow(Eq.MaxFreq / Eq.MinFreq, i / 240.0);
                var p = new Point(Left + PlotW * i / 240.0, Y(Math.Clamp(EqMath.TotalDb(bands, f), -12, 12)));
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(accent, 2.5, lineJoin: PenLineJoin.Round), total);

        for (var i = 0; i < bands.Count; i++)
        {
            var on = i == _panel.SelectedIndex;
            var radius = (on ? 9 : 6) + (i == _hover || i == _drag ? 2 : 0);
            var c = new Point(X(bands[i].Freq), Y(bands[i].Gain));
            ctx.DrawEllipse(on ? accent : card, new Pen(accent, 2), c, radius, radius);
            var n = Text((i + 1).ToString(), on ? 11 : 9, on ? card : accent);
            ctx.DrawText(n, new Point(c.X - n.Width / 2, c.Y - n.Height / 2));
        }

        var s = _panel.Selected;
        var info = Text($"Band {s.Number} Â· {s.FreqText} Â· {s.GainTextDb} Â· Q {s.QText}", 12, muted);
        ctx.DrawText(info, new Point(Bounds.Width - Right - 4 - info.Width, Top + 2));
    }

    // ---- mouse ----

    private int NodeAt(Point p)
    {
        if (_panel is null) return -1;
        var best = -1;
        var bestDist = 16.0;
        for (var i = 0; i < Eq.Bands; i++)
        {
            var b = _panel.Bands[i];
            var d = Math.Sqrt(Math.Pow(X(b.Freq) - p.X, 2) + Math.Pow(Y(b.Gain) - p.Y, 2));
            if (d < bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hit = NodeAt(e.GetPosition(this));
        if (hit < 0 || _panel is null) return;
        _panel.SelectedIndex = hit;
        _drag = hit;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panel is null) return;
        var p = e.GetPosition(this);
        if (_drag >= 0)
        {
            var b = _panel.Bands[_drag];
            b.Freq = FreqAt(p.X);
            b.Gain = Math.Round(DbAt(p.Y) * 2) / 2;
            return;
        }
        var hover = NodeAt(p);
        if (hover != _hover) { _hover = hover; Cursor = new Cursor(hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow); InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        _drag = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover >= 0) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_panel is null) return;
        _panel.Selected.Q *= e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        e.Handled = true;
    }
}

