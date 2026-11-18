using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine.Dsp;

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
    private const double LabelSize = DrawKit.LabelSize, InfoSize = DrawKit.InfoSize + 1, HoverGrow = 2, HitRadius = 16;

    /// <summary>The band circle's radius: half of <c>EqBandCircle</c> (App.axaml).</summary>
    private double PointRadius => Size("EqBandCircle", 22) / 2;

    private double Size(string key, double fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is double d ? d : fallback;
    private const int CurvePoints = 240;
    private const double SpectrumOpacity = 0.25;
    private static readonly double[] CurveFreqs = EqMath.LogGrid(CurvePoints + 1, Eq.MinFreq, Eq.MaxFreq);
    private const double PhaseRange = 180, DragGainStep = 0.5;
    private static readonly double[] PhaseLabels = { PhaseRange, 0, -PhaseRange };

    private int _drag = -1, _hover = -1;
    private bool _widthDrag;

    public EqGraph() => ClipToBounds = true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Item?.WatchSpectrum(true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Item?.WatchSpectrum(false); // the capture is released as soon as the graph is not on screen
        base.OnDetachedFromVisualTree(e);
    }

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

        DrawSpectrum(ctx, Item.Spectrum, Brush("BrushMuted"));
        var bands = Item.Export();
        if (Item.Selected is { } selected) DrawOwnShare(ctx, bands[selected.Index], Brush("BrushAccentSoft"));
        if (Item.ShowPhase) DrawPhase(ctx, bands, muted); // over the shading, so the shade never hides the phase line
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

    /// <summary>The live spectrum behind everything else: a faint filled shape in the muted brush, from the bottom of the plot up.</summary>
    private void DrawSpectrum(DrawingContext ctx, float[]? levels, IBrush muted)
    {
        if (levels is null) return;
        var area = Frame.Area;
        var ratio = Eq.MaxFreq / Eq.MinFreq;
        var points = new Point[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            var hz = Eq.MinFreq * Math.Pow(ratio, (i + 0.5) / levels.Length);
            var share = Math.Clamp((levels[i] - SpectrumAnalyzer.FloorDb) / -SpectrumAnalyzer.FloorDb, 0, 1);
            points[i] = new Point(X(hz), area.Bottom - area.Height * share);
        }
        using (ctx.PushOpacity(SpectrumOpacity)) ctx.DrawGeometry(muted, null, ResponsePlot.Fill(points, area.Bottom));
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
        DrawWidthHandles(ctx, accent);
        foreach (var b in Item.Bands)
        {
            var on = b.Index == Item.SelectedIndex;
            var ink = EqBandColors.Of(this, b.Index);
            var radius = PointRadius + (b.Index == _hover || b.Index == _drag ? HoverGrow : 0);
            var c = PointOf(b);
            if (on) ctx.DrawEllipse(null, new Pen(accent, 2), c, radius + 3, radius + 3);
            var edge = b.Enabled ? new Pen(ink, 2) : new Pen(ink, 2, dashStyle: DashStyle.Dash);
            ctx.DrawEllipse(b.Enabled ? ink : card, edge, c, radius, radius);
            // the circle is too small to hold its number: the selected band's title is in the info line, the list shows the numbers
        }
    }

    // ---- bandwidth handles (the selected band only) ----

    /// <summary>The selected band when it has a bandwidth the handles can change, else null.</summary>
    private EqBandItem? HandleBand => Item?.Selected is { UsesBandwidth: true } b ? b : null;

    /// <summary>Where a handle sits: at the band's height, on the band's edge frequency (half its bandwidth either side, in octaves).</summary>
    private Rect HandleRect(EqBandItem b, int side)
    {
        var w = Size("EqWidthHandleWidth", 8);
        var h = Size("EqWidthHandleHeight", 12);
        var edge = Math.Clamp(b.Freq * Math.Pow(2, side * b.Bandwidth / 2), Eq.MinFreq, Eq.MaxFreq);
        var c = PointOf(b);
        return new Rect(X(edge) - w / 2, c.Y - h / 2, w, h);
    }

    private void DrawWidthHandles(DrawingContext ctx, IBrush accent)
    {
        if (HandleBand is not { } b) return;
        var left = HandleRect(b, -1);
        var right = HandleRect(b, 1);
        var line = new Pen(Brush("BrushAccentEdge"), 1, dashStyle: DashStyle.Dash);
        ctx.DrawLine(line, left.Center, right.Center);
        foreach (var r in new[] { left, right }) ctx.DrawRectangle(Brush("BrushHover"), new Pen(accent, 1), r, 3, 3);
    }

    /// <summary>-1 or 1 for the handle under <paramref name="p"/>, else 0.</summary>
    private int HandleAt(Point p)
    {
        if (HandleBand is not { } b) return 0;
        if (HandleRect(b, -1).Inflate(3).Contains(p)) return -1;
        return HandleRect(b, 1).Inflate(3).Contains(p) ? 1 : 0;
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
        if (HandleOver(e.GetPosition(this)) != 0 && HandleBand is { } handled)
        {
            e.Handled = true;
            if (e.ClickCount == 2) { handled.ResetWidth(); return; }
            _widthDrag = true;
            e.Pointer.Capture(this);
            return;
        }
        var hit = PointAt(e.GetPosition(this));
        if (hit < 0 || Item is null) return;
        Item.SelectedIndex = hit;
        e.Handled = true;
        if (e.ClickCount == 2) // a double-click turns the band on or off and must not move it
        {
            Item.Bands[hit].ToggleEnabled();
            return;
        }
        _drag = hit;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Item is null) return;
        var p = e.GetPosition(this);
        if (_widthDrag)
        {
            HandleBand?.SetWidthFromEdge(FreqAt(p.X));
            return;
        }
        if (_drag >= 0 && _drag < Item.Bands.Count)
        {
            var b = Item.Bands[_drag];
            b.Freq = FreqAt(p.X);
            if (b.UsesGain) b.Gain = Math.Round(DbAt(p.Y) / DragGainStep) * DragGainStep;
            return;
        }
        UpdateCursor(p);
        var hover = PointAt(p);
        if (hover == _hover) return;
        _hover = hover;
        InvalidateVisual();
    }

    /// <summary>The handle under <paramref name="p"/> unless the band's own circle is there (a narrow band's handles sit against it).</summary>
    private int HandleOver(Point p)
    {
        if (HandleBand is not { } b) return 0;
        var c = PointOf(b);
        return Math.Sqrt(Math.Pow(c.X - p.X, 2) + Math.Pow(c.Y - p.Y, 2)) <= PointRadius ? 0 : HandleAt(p);
    }

    private void UpdateCursor(Point p) =>
        Cursor = new Cursor(_widthDrag || HandleOver(p) != 0 ? StandardCursorType.SizeWestEast : PointAt(p) >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_widthDrag)
        {
            _widthDrag = false;
            e.Pointer.Capture(null);
            return;
        }
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
