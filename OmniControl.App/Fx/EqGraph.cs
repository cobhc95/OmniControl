using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx;

/// <summary>
/// Frequency-response graph of the equaliser with one numbered, draggable point per band (x = frequency, y = gain; the wheel over a
/// point changes its bandwidth). Draws and handles input only: all values live in <see cref="EqPanelItem"/> (its DataContext),
/// whose maths (<see cref="EqMath"/>) is the engine's. Keyboard control is the card's (see <c>ISideNavigable</c>).
/// </summary>
public sealed class EqGraph : ItemDrawing<EqPanelItem>
{
    private const double Left = ResponsePlot.AxisLeft, Top = ResponsePlot.AxisTop, Bottom = ResponsePlot.AxisBottom;
    private const double RightPlain = ResponsePlot.AxisRight, RightPhase = 40;
    private const double LabelSize = DrawKit.LabelSize, InfoSize = DrawKit.InfoSize, PointRadius = 6, SelectedRadius = 9, HoverGrow = 2, HitRadius = 16;
    private const int CurvePoints = 240;
    private static readonly double[] CurveFreqs = EqMath.LogGrid(CurvePoints + 1, Eq.MinFreq, Eq.MaxFreq);
    private const double PhaseRange = 180, DragGainStep = 0.5;
    private static readonly double[] PhaseLabels = { PhaseRange, 0, -PhaseRange };

    private int _drag = -1, _hover = -1;

    public EqGraph() => ClipToBounds = true;

    // ---- geometry ----

    private double Right => Item?.ShowPhase == true ? RightPhase : RightPlain;
    private double PlotW => Math.Max(1, Bounds.Width - Left - Right);
    private double PlotH => Math.Max(1, Bounds.Height - Top - Bottom);
    private PlotFrame Frame => new(new Rect(Left, Top, PlotW, PlotH), Eq.MinFreq, Eq.MaxFreq, Eq.MaxGain, Eq.MinGain);
    private double X(double freq) => Frame.X(freq);
    private double Y(double db) => Frame.Y(db);
    private double PhaseY(double deg) => Top + PlotH * (PhaseRange - deg) / (2 * PhaseRange);
    private double FreqAt(double x) => Frame.HzAt(x);
    private double DbAt(double y) => Frame.DbAt(y);

    /// <summary>Where a band's point sits: at its gain, or on the 0 dB line for types without gain.</summary>
    private Point PointOf(EqBandItem b) => new(X(b.Freq), Y(b.UsesGain ? b.Gain : 0));

    private IBrush Brush(string key) => DrawKit.Brush(this, key);

    private static FormattedText Text(string s, double size, IBrush brush) => DrawKit.Text(s, size, brush);

    // ---- drawing ----

    public override void Render(DrawingContext ctx)
    {
        var accent = Brush("BrushAccent");
        var muted = Brush("BrushMuted");
        var card = Brush("BrushBackground");
        var divider = Brush("BrushDivider");

        ctx.DrawRectangle(card, new Pen(divider, 1), new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1), 8, 8);
        DrawScale(ctx, muted, divider);
        if (Item is null) return;

        var bands = Item.Export();
        if (Item.ShowPhase) DrawPhase(ctx, bands, muted);
        if (Item.Selected is { } selected) DrawOwnShare(ctx, bands[selected.Index], Brush("BrushAccentSoft"));
        DrawCurve(ctx, bands, accent);
        DrawPoints(ctx, accent, muted, card);

        if (Item.Selected is { } s)
        {
            var info = Text($"{s.Title} · {s.TypeName} · {s.FreqText}{(s.UsesGain ? " · " + s.GainTextDb : "")} · {s.BandwidthText}", InfoSize, muted);
            ctx.DrawText(info, new Point(Math.Max(Left, Bounds.Width - Right - 4 - info.Width), Top + 2));
        }
    }

    private void DrawScale(DrawingContext ctx, IBrush muted, IBrush divider)
    {
        var fine = Item?.ShowGrid != false ? new Pen(divider, 0.5) : null;
        ResponsePlot.FreqAxis(ctx, Frame, fine, muted, Bounds.Width);
        ResponsePlot.DbAxis(ctx, Frame, new Pen(muted, 1), fine, muted);
        if (Item?.ShowPhase != true) return;
        foreach (var deg in PhaseLabels)
        {
            var label = Text($"{deg:+0;-0;0}°", LabelSize, muted);
            ctx.DrawText(label, new Point(Left + PlotW + 4, Math.Clamp(PhaseY(deg) - label.Height / 2, 0, Bounds.Height - label.Height)));
        }
    }

    /// <summary>The selected band's own contribution, shaded down to 0 dB.</summary>
    private void DrawOwnShare(DrawingContext ctx, EqBand band, IBrush soft) =>
        ctx.DrawGeometry(soft, null, ResponsePlot.Fill(Frame.Points(CurveFreqs, EqMath.CurveDb(new[] { band }, 0, CurveFreqs)), Y(0)));

    /// <summary>The combined response of all bands and the overall gain.</summary>
    private void DrawCurve(DrawingContext ctx, IReadOnlyList<EqBand> bands, IBrush accent) =>
        ctx.DrawGeometry(null, new Pen(accent, 2.5, lineJoin: PenLineJoin.Round),
            ResponsePlot.Line(Frame.Points(CurveFreqs, EqMath.CurveDb(bands, Item?.OverallGainDb ?? 0, CurveFreqs))));

    private void DrawPhase(DrawingContext ctx, IReadOnlyList<EqBand> bands, IBrush muted) =>
        ctx.DrawGeometry(null, new Pen(muted, 1.2, dashStyle: DashStyle.Dash),
            ResponsePlot.Line(CurveFreqs.Select(f => new Point(X(f), PhaseY(EqMath.PhaseDeg(EqMath.Response(bands, f))))).ToList()));

    private void DrawPoints(DrawingContext ctx, IBrush accent, IBrush muted, IBrush card)
    {
        if (Item is null) return;
        foreach (var b in Item.Bands)
        {
            var on = b.Index == Item.SelectedIndex;
            var ink = b.Enabled ? accent : muted;
            var radius = (on ? SelectedRadius : PointRadius) + (b.Index == _hover || b.Index == _drag ? HoverGrow : 0);
            var c = PointOf(b);
            ctx.DrawEllipse(on ? ink : card, new Pen(ink, 2), c, radius, radius);
            var n = Text(b.Number, on ? LabelSize : LabelSize - 2, on ? card : ink);
            ctx.DrawText(n, new Point(c.X - n.Width / 2, c.Y - n.Height / 2));
        }
    }

    // ---- mouse ----

    /// <summary>The band whose point is nearest <paramref name="p"/> within reach; the selected one wins a tie so it stays grabbable.</summary>
    private int PointAt(Point p)
    {
        if (Item is null) return -1;
        var best = -1;
        var bestDist = HitRadius;
        foreach (var b in Item.Bands)
        {
            var c = PointOf(b);
            var d = Math.Sqrt(Math.Pow(c.X - p.X, 2) + Math.Pow(c.Y - p.Y, 2)) - (b.IsSelected ? 0.5 : 0);
            if (d < bestDist) { best = b.Index; bestDist = d; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hit = PointAt(e.GetPosition(this));
        if (hit < 0 || Item is null) return;
        Item.SelectedIndex = hit;
        _drag = hit;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Item is null) return;
        var p = e.GetPosition(this);
        if (_drag >= 0 && _drag < Item.Bands.Count)
        {
            var b = Item.Bands[_drag];
            b.Freq = FreqAt(p.X);
            if (b.UsesGain) b.Gain = Math.Round(DbAt(p.Y) / DragGainStep) * DragGainStep;
            return;
        }
        var hover = PointAt(p);
        if (hover == _hover) return;
        _hover = hover;
        Cursor = new Cursor(hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        InvalidateVisual();
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

    /// <summary>The wheel over a point changes that band's bandwidth; elsewhere it scrolls the editor as usual.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var hit = PointAt(e.GetPosition(this));
        if (hit < 0 || Item is null) return;
        Item.SelectedIndex = hit;
        Item.Bands[hit].NudgeWidth(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }
}
