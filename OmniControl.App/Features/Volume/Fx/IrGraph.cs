using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Fx;

/// The IR Loader's picture, drawn as TabForge draws it (MIT): the impulse response's waveform in the top part and, below it, its
/// frequency response from 20 Hz to 20 kHz (the loudest frequency at 0 dB down to -54 dB, the 0 dB line and the 100 Hz / 1 kHz / 10 kHz
/// marks), or the hint while there is no data. Draws only; the data is on its <see cref="IrFileItem"/>. The geometry is built again only
/// when the data or the size changed.
public sealed class IrGraph : ItemDrawing<IrFileItem>
{
    private const double TopDb = 6, BottomDb = -54, WaveShare = 0.4, WavePad = 4, GridThickness = 1, CurveThickness = 1.5, LabelInset = 3;
    private static readonly double[] MarkedHz = { 100, 1000, 10000 };

    private Size _built;
    private float[]? _builtFrom;
    private StreamGeometry? _wave, _response;

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (Item is not { } item || size.Width < 4 || size.Height < 4) return;
        var grid = new Pen(DrawKit.Brush(this, "BrushDivider"), GridThickness);
        var muted = DrawKit.Brush(this, "BrushMuted");
        ctx.DrawRectangle(DrawKit.Brush(this, "BrushBackground"), grid, new Rect(size).Deflate(GridThickness / 2));
        if (item.Peaks.Length == 0)
        {
            var hint = DrawKit.Text(item.Hint, DrawKit.LabelSize, muted);
            ctx.DrawText(hint, new Point(Math.Max(LabelInset, (size.Width - hint.Width) / 2), (size.Height - hint.Height) / 2));
            return;
        }
        if (_built != size || !ReferenceEquals(_builtFrom, item.Peaks)) Build(item, size);
        var half = size.Height * WaveShare;
        ctx.DrawLine(grid, new Point(0, half), new Point(size.Width, half));
        var zero = half + (size.Height - half) * (TopDb / (TopDb - BottomDb));
        ctx.DrawLine(grid, new Point(0, zero), new Point(size.Width, zero));
        foreach (var hz in MarkedHz)
        {
            var x = Math.Log(hz / IrPictures.LowHz) / Math.Log(IrPictures.HighHz / IrPictures.LowHz) * size.Width;
            ctx.DrawLine(grid, new Point(x, half), new Point(x, size.Height));
            var label = DrawKit.Text(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", DrawKit.LabelSize, muted);
            ctx.DrawText(label, new Point(x + LabelInset, size.Height - label.Height - LabelInset));
        }
        ctx.DrawText(DrawKit.Text("0 dB", DrawKit.LabelSize, muted), new Point(LabelInset, zero + 1));
        var accent = DrawKit.Brush(this, "BrushAccent");
        if (_wave is not null) ctx.DrawGeometry(DrawKit.Brush(this, "BrushAccentSoft"), new Pen(accent, GridThickness), _wave);
        if (_response is not null) ctx.DrawGeometry(null, new Pen(accent, CurveThickness), _response);
    }

    private void Build(IrFileItem item, Size size)
    {
        (_built, _builtFrom) = (size, item.Peaks);
        var half = size.Height * WaveShare;
        var mid = half / 2;
        var peak = item.Peaks.Max();
        var scale = peak > 0 ? (mid - WavePad) / peak : 0;
        _wave = new StreamGeometry();
        using (var g = _wave.Open())
        {
            g.BeginFigure(new Point(0, mid), true);
            for (var i = 0; i < item.Peaks.Length; i++) g.LineTo(new Point(X(i, item.Peaks.Length, size.Width), mid - item.Peaks[i] * scale));
            for (var i = item.Peaks.Length - 1; i >= 0; i--) g.LineTo(new Point(X(i, item.Peaks.Length, size.Width), mid + item.Peaks[i] * scale));
            g.EndFigure(true);
        }
        _response = null;
        if (item.Curve.Length < 2) return;
        _response = new StreamGeometry();
        using var r = _response.Open();
        for (var i = 0; i < item.Curve.Length; i++)
        {
            var db = Math.Clamp(item.Curve[i], BottomDb, TopDb);
            var point = new Point(X(i, item.Curve.Length, size.Width), half + (size.Height - half) * ((TopDb - db) / (TopDb - BottomDb)));
            if (i == 0) r.BeginFigure(point, false); else r.LineTo(point);
        }
        r.EndFigure(false);
    }

    private static double X(int i, int count, double width) => count <= 1 ? 0 : i * width / (count - 1);
}
