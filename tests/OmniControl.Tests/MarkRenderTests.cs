using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OmniControl.App.Features.Screenshot;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// The area overlay's drawing tools end to end without a window: pointer calls on the view-model, then the bright picture painted by the
/// overlay's own <see cref="MarkPainter"/> into a copy of the picture, as the overlay's surface holds it. Every tool must leave marks, in
/// every drag direction and after a blur, and painting only what changed must give what one full drawing gives. Avalonia runs on one STA
/// thread of its own.
public class MarkRenderTests
{
    private const int Size = 200, Grey = 128;

    private static readonly BlockingCollection<Action> Work = new();

    static MarkRenderTests()
    {
        var ui = new Thread(() =>
        {
            AppBuilder.Configure<Application>().UseWin32().UseSkia().SetupWithoutStarting();
            foreach (var job in Work.GetConsumingEnumerable()) job();
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
    }

    private static T OnUi<T>(Func<T> job)
    {
        T? result = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        Work.Add(() =>
        {
            try { result = job(); }
            catch (Exception e) { error = e; }
            done.Set();
        });
        done.Wait();
        if (error is not null) throw new InvalidOperationException("The drawing failed.", error);
        return result!;
    }

    private static ScreenImage Picture(bool stripes = false)
    {
        var pixels = new byte[Size * Size * ScreenImage.BytesPerPixel];
        Array.Fill(pixels, (byte)Grey);
        if (stripes)
            for (var i = 0; i < pixels.Length; i += ScreenImage.BytesPerPixel) pixels[i] = (byte)(i / ScreenImage.BytesPerPixel % 2 == 0 ? 0 : 255);
        return new ScreenImage(Size, Size, pixels);
    }

    private sealed class NoOutput : IShotOutput
    {
        public void Copy(ScreenImage image) { }
        public void Save(ScreenImage image) { }
        public void Print(ScreenImage image) { }
        public void Record(ScreenRect area, ScreenRect monitor, double scale) { }
        public void SearchOnline(ScreenImage image) { }
        public Task<IReadOnlyList<string>> ReadQrAsync(ScreenImage image) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public void CopyText(string text) { }
        public void OpenLink(string url) { }
    }

    private static void Drag(CaptureOverlayViewModel model, DrawTool tool, Point from, Point to)
    {
        model.Tool = tool;
        model.Press(from, 4);
        model.MoveTo(to);
        model.Release(to);
    }

    /// A model with almost the whole picture selected (only drawings differ from the picture in what is painted).
    private static CaptureOverlayViewModel Model(bool stripes = false)
    {
        var model = new CaptureOverlayViewModel(ImageBitmaps.ToBitmap(Picture(stripes)), new ScreenRect(0, 0, Size, Size), new NoOutput(), () => { }, false,
            new ToolSizes(new Dictionary<string, double>(), () => { })) { Scale = 1 };
        Drag(model, DrawTool.None, new Point(10, 10), new Point(190, 190));
        return model;
    }

    /// The bright picture as the overlay's surface holds it: a copy of the picture that the painter writes its repainted parts into. Kept
    /// across changes, it shows what painting only the changed parts gives.
    private sealed class Screen
    {
        private readonly ScreenImage _image;
        private readonly MarkPainter _painter;

        public Screen(CaptureOverlayViewModel model)
        {
            _image = ImageBitmaps.Crop(model.Picture, new ScreenRect(0, 0, Size, Size));
            _painter = new MarkPainter(model.Picture, Write);
        }

        private void Write(ScreenRect area, ScreenImage part)
        {
            for (var y = 0; y < area.Height; y++)
                Buffer.BlockCopy(part.Pixels, y * part.Stride, _image.Pixels, (area.Y + y) * _image.Stride + area.X * ScreenImage.BytesPerPixel, part.Stride);
        }

        public byte[] Show(CaptureOverlayViewModel model)
        {
            _painter.Update(model.Marks, model.Preview);
            return (byte[])_image.Pixels.Clone();
        }
    }

    /// The picture with its drawings as a fresh overlay would paint it.
    private static byte[] Render(CaptureOverlayViewModel model) => new Screen(model).Show(model);

    /// The picture with its drawings drawn whole in one go (the drawing in progress too).
    private static byte[] Whole(CaptureOverlayViewModel model) =>
        ImageBitmaps.Render(model.Picture, new ScreenRect(0, 0, Size, Size), model.Marks, model.Preview is { } p && !model.Marks.Contains(p) ? p : null).Pixels;

    private static int Worst(byte[] a, byte[] b) => Enumerable.Range(0, a.Length).Max(i => Math.Abs(a[i] - b[i]));

    /// How many pixels of the painted picture are not the plain grey picture.
    private static int Changed(CaptureOverlayViewModel model)
    {
        var pixels = Render(model);
        var changed = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] != Grey || pixels[i + 1] != Grey || pixels[i + 2] != Grey) changed++;
        return changed;
    }

    private static int AfterBlur(Action<CaptureOverlayViewModel>? then) => OnUi(() =>
    {
        var model = Model();
        Drag(model, DrawTool.Blur, new Point(150, 150), new Point(180, 180));
        then?.Invoke(model);
        return Changed(model);
    });

    /// Two marker strokes over each other (one crossing itself too) are no darker where they overlap than a single stroke is.
    [Fact]
    public void MarkerOverlapIsAsStrongAsOneStroke() => OnUi<int>(() =>
    {
        var picture = ImageBitmaps.ToBitmap(Picture());
        var all = new ScreenRect(0, 0, Size, Size);
        StrokeMark Stroke(Point from, Point to, params Point[] more)
        {
            var mark = new StrokeMark(from, Colors.Yellow, 20, MarkLook.MarkerOpacity);
            mark.Extend(to);
            foreach (var p in more) mark.Extend(p);
            return mark;
        }
        var single = ImageBitmaps.Render(picture, all, new[] { Stroke(new Point(20, 100), new Point(180, 100)) }).Pixels;
        var crossing = ImageBitmaps.Render(picture, all,
            new[] { Stroke(new Point(20, 100), new Point(180, 100)), Stroke(new Point(100, 20), new Point(100, 180)), Stroke(new Point(20, 100), new Point(180, 100)) }).Pixels;
        var at = (100 * Size + 100) * ScreenImage.BytesPerPixel; // the crossing of the strokes
        var one = (100 * Size + 40) * ScreenImage.BytesPerPixel; // on a single stroke only
        Assert.Equal(single[one], crossing[at]);
        Assert.Equal(single[one + 2], crossing[at + 2]);
        Assert.NotEqual(Grey, single[one]); // the stroke did colour the picture
        return 0;
    });

    [Fact]
    public void NothingDrawn_LeavesThePictureAlone() => Assert.Equal(0, OnUi(() => Changed(Model())));

    [Theory]
    [InlineData(DrawTool.Pen)]
    [InlineData(DrawTool.Line)]
    [InlineData(DrawTool.Arrow)]
    [InlineData(DrawTool.Box)]
    [InlineData(DrawTool.Marker)]
    public void EveryToolDrawsInEveryDragDirection(DrawTool tool)
    {
        foreach (var (from, to) in new[]
                 {
                     (new Point(50, 50), new Point(150, 120)), (new Point(150, 120), new Point(50, 50)),
                     (new Point(50, 120), new Point(150, 50)), (new Point(150, 50), new Point(50, 120)),
                 })
            Assert.True(OnUi(() => { var m = Model(); Drag(m, tool, from, to); return Changed(m); }) > 0, $"{tool} from {from} to {to} drew nothing");
    }

    [Fact]
    public void Text_Draws() => Assert.True(OnUi(() => { var m = Model(); m.AddText(new Point(50, 50), "Hello"); return Changed(m); }) > 0);

    [Fact]
    public void Blur_ChangesThePicture() =>
        Assert.True(OnUi(() =>
        {
            var m = Model(stripes: true);
            var before = Render(m);
            Drag(m, DrawTool.Blur, new Point(60, 60), new Point(140, 140));
            return !Render(m).SequenceEqual(before);
        }));

    [Theory]
    [InlineData(DrawTool.Pen)]
    [InlineData(DrawTool.Line)]
    [InlineData(DrawTool.Arrow)]
    [InlineData(DrawTool.Box)]
    [InlineData(DrawTool.Marker)]
    public void ToolsStillDrawAfterABlur(DrawTool tool)
    {
        var blurredOnly = AfterBlur(null);
        Assert.True(AfterBlur(m => Drag(m, tool, new Point(30, 30), new Point(100, 90))) > blurredOnly, $"{tool} after a blur added nothing");
    }

    [Fact]
    public void TextStillDrawsAfterABlur() => Assert.True(AfterBlur(m => m.AddText(new Point(30, 30), "Hello")) > AfterBlur(null));

    /// A long free-hand line (several settled parts and a growing rest, a zigzag so the parts meet at turns) looks the same while it is
    /// drawn as once it is finished; for the see-through marker too, where parts meeting must not be darker.
    [Theory]
    [InlineData(DrawTool.Pen)]
    [InlineData(DrawTool.Marker)]
    public void LongStroke_LooksTheSameWhileDrawnAsWhenFinished(DrawTool tool)
    {
        var (drawing, finished) = OnUi(() =>
        {
            var m = Model();
            m.Tool = tool;
            m.Press(new Point(20, 20), 4);
            var last = new Point(20, 20);
            for (var i = 1; i <= 150; i++) m.MoveTo(last = new Point(20 + i, 20 + (i % 20 < 10 ? i % 10 : 10 - i % 10) * 15));
            var whileDrawn = Render(m);
            m.Release(last);
            return (whileDrawn, Render(m));
        });
        // Only anti-aliasing may differ: Skia picks another edge rasteriser for a short path than for a long one (up to about 24 of 255 on
        // an edge pixel of the solid pen). Marker parts faded each on their own would be about 30 darker where they meet.
        var allowed = tool == DrawTool.Marker ? 16 : 32;
        var worst = Worst(drawing, finished);
        Assert.True(worst <= allowed, $"{tool}: a colour differs by {worst} while drawn");
    }

    /// Painting only where drawings changed, after every pointer move, gives what drawing everything at once gives: a line or box that
    /// moved leaves nothing behind, a free-hand line misses no piece, undo and redo clean up. Only anti-aliasing may differ a little.
    [Theory]
    [InlineData(DrawTool.Pen)]
    [InlineData(DrawTool.Marker)]
    [InlineData(DrawTool.Line)]
    [InlineData(DrawTool.Arrow)]
    [InlineData(DrawTool.Box)]
    [InlineData(DrawTool.Blur)]
    public void PaintingOnlyWhatChanged_MatchesDrawingItWhole(DrawTool tool)
    {
        var worst = OnUi(() =>
        {
            var m = Model(stripes: true);
            var screen = new Screen(m);
            var worst = 0;
            void Check() => worst = Math.Max(worst, Worst(screen.Show(m), Whole(m)));
            m.Tool = tool;
            m.Press(new Point(40, 40), 4);
            for (var i = 1; i <= 140; i++)
            {
                m.MoveTo(new Point(40 + i, 40 + (i % 30 < 15 ? i % 15 : 15 - i % 15) * 8)); // to and fro, so earlier places must be cleaned up
                if (i % 7 == 0) Check(); // several moves between two frames, as with a fast mouse
            }
            Check();
            m.Release(new Point(180, 60));
            Check();
            m.HandleKey(Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Control);
            Check();
            m.HandleKey(Avalonia.Input.Key.Y, Avalonia.Input.KeyModifiers.Control);
            Check();
            return worst;
        });
        Assert.True(worst <= 32, $"{tool}: painting only what changed differs by {worst}");
    }

    /// A colour picked from the colour pop-down is the colour of the next drawing; drawings made before keep theirs.
    [Fact]
    public void PickedColour_IsUsedForNewDrawingsOnly()
    {
        var (before, after) = OnUi(() =>
        {
            var m = Model();
            Drag(m, DrawTool.Box, new Point(30, 30), new Point(60, 60));
            var first = Render(m);
            var pick = m.Colours[1];
            pick.Pick.Execute(null);
            Assert.Equal(((Avalonia.Media.ISolidColorBrush)pick.Brush).Color, ((Avalonia.Media.ISolidColorBrush)m.CurrentBrush).Color); // the colour button's dot
            Drag(m, DrawTool.Box, new Point(100, 100), new Point(140, 140));
            return (first, Render(m));
        });
        var at = (30 * Size + 45) * 4; // on the first box's top side: unchanged
        Assert.Equal(before.Skip(at).Take(3), after.Skip(at).Take(3));
        var second = (100 * Size + 120) * 4; // on the second box's top side: the picked colour
        var colour = OmniControl.App.Shared.Services.Palette.Drawing[1];
        Assert.Equal(new[] { colour.B, colour.G, colour.R }, after.Skip(second).Take(3).ToArray());
    }

    [Theory]
    [InlineData(10, 10, 60, 40, 10, 10, 50, 30)]
    [InlineData(60, 40, 10, 10, 10, 10, 50, 30)]
    [InlineData(10, 40, 60, 10, 10, 10, 50, 30)]
    [InlineData(60, 10, 10, 40, 10, 10, 50, 30)]
    [InlineData(5, 5, 5, 5, 5, 5, 0, 0)]
    public void Enclosing_IsTheSameRectangleWhicheverWayTheDragWent(double ax, double ay, double bx, double by, double x, double y, double w, double h) =>
        Assert.Equal(new Rect(x, y, w, h), MarkMath.Enclosing(new Point(ax, ay), new Point(bx, by)));
}
