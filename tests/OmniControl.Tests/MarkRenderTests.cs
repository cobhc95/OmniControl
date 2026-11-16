using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using OmniControl.App.Screenshot;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// <summary>
/// The area overlay's drawing tools end to end without a window: pointer calls on the view-model, then the real canvas drawn into a
/// bitmap. Every tool must leave marks, in every drag direction and after a blur. Avalonia runs on one STA thread of its own.
/// </summary>
public class MarkRenderTests
{
    private const int Size = 200, Grey = 128;

    private static readonly BlockingCollection<Action> Work = new();

    static MarkRenderTests()
    {
        var ui = new Thread(() =>
        {
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
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

    /// <summary>A model with almost the whole picture selected (the canvas has no style here, so no frame is drawn and only drawings differ from the picture).</summary>
    private static CaptureOverlayViewModel Model(bool stripes = false)
    {
        var model = new CaptureOverlayViewModel(Picture(stripes), new ScreenRect(0, 0, Size, Size), new NoOutput(), () => { }, false,
            new ToolSizes(new Dictionary<string, double>(), () => { })) { Scale = 1 };
        Drag(model, DrawTool.None, new Point(10, 10), new Point(190, 190));
        return model;
    }

    /// <summary>The canvas drawn into pixels.</summary>
    private static byte[] Render(CaptureOverlayViewModel model)
    {
        var canvas = new CaptureCanvas { Model = model, Width = Size, Height = Size };
        canvas.Measure(new Size(Size, Size));
        canvas.Arrange(new Rect(0, 0, Size, Size));
        using var target = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        target.Render(canvas);
        var pixels = new byte[Size * Size * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(0, 0, Size, Size), pin.AddrOfPinnedObject(), pixels.Length, Size * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    /// <summary>How many pixels of the drawn canvas are not the plain grey picture.</summary>
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

    [Theory]
    [InlineData(10, 10, 60, 40, 10, 10, 50, 30)]
    [InlineData(60, 40, 10, 10, 10, 10, 50, 30)]
    [InlineData(10, 40, 60, 10, 10, 10, 50, 30)]
    [InlineData(60, 10, 10, 40, 10, 10, 50, 30)]
    [InlineData(5, 5, 5, 5, 5, 5, 0, 0)]
    public void Enclosing_IsTheSameRectangleWhicheverWayTheDragWent(double ax, double ay, double bx, double by, double x, double y, double w, double h) =>
        Assert.Equal(new Rect(x, y, w, h), MarkMath.Enclosing(new Point(ax, ay), new Point(bx, by)));
}
