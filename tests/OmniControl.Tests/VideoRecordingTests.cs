using OmniControl.App.Screenshot;
using OmniControl.Core.Engine.Screenshot;
using SkiaSharp;

namespace OmniControl.Tests;

/// <summary>
/// The screen recorder's pure parts: even frame sizes, the bitrate, the frame grid and the recording clock, a GIF's delays, palette and
/// file (read back with SkiaSharp), the drawing tools' size steps and the recording bar's timer. Nothing is captured, encoded to MP4 or shown.
/// </summary>
public class VideoRecordingTests
{
    private const long Second = VideoMath.TicksPerSecond;

    // ---- Even sizes ----

    [Theory]
    [InlineData(101, 100)]
    [InlineData(100, 100)]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    public void Even_RoundsDownToEvenAtLeastTwo(int side, int even) => Assert.Equal(even, VideoMath.Even(side));

    [Fact]
    public void FrameArea_Mp4IsEvenGifIsAsDrawn()
    {
        var area = new ScreenRect(10, 20, 301, 199);
        Assert.Equal(new ScreenRect(10, 20, 300, 198), VideoMath.FrameArea(area, VideoFormat.Mp4H264));
        Assert.Equal(new ScreenRect(10, 20, 300, 198), VideoMath.FrameArea(area, VideoFormat.Mp4Hevc));
        Assert.Equal(area, VideoMath.FrameArea(area, VideoFormat.Gif));
    }

    // ---- Bitrate ----

    [Fact]
    public void Bitrate_IsPixelsPerSecondTimesQuality() =>
        Assert.Equal(4_976_640, VideoMath.Bitrate(1920, 1080, 30, VideoQuality.Medium, VideoFormat.Mp4H264));

    [Fact]
    public void Bitrate_HevcNeedsLess() =>
        Assert.True(VideoMath.Bitrate(1920, 1080, 30, VideoQuality.High, VideoFormat.Mp4Hevc) < VideoMath.Bitrate(1920, 1080, 30, VideoQuality.High, VideoFormat.Mp4H264));

    [Fact]
    public void Bitrate_GrowsWithQuality()
    {
        var low = VideoMath.Bitrate(1280, 720, 30, VideoQuality.Low, VideoFormat.Mp4H264);
        var medium = VideoMath.Bitrate(1280, 720, 30, VideoQuality.Medium, VideoFormat.Mp4H264);
        var high = VideoMath.Bitrate(1280, 720, 30, VideoQuality.High, VideoFormat.Mp4H264);
        Assert.True(low < medium && medium < high);
    }

    [Fact]
    public void Bitrate_IsHeldToTheLimits()
    {
        Assert.Equal(VideoMath.MinBitrate, VideoMath.Bitrate(16, 16, 15, VideoQuality.Low, VideoFormat.Mp4H264));
        Assert.Equal(VideoMath.MaxBitrate, VideoMath.Bitrate(7680, 4320, 60, VideoQuality.High, VideoFormat.Mp4H264));
    }

    // ---- Frame timing ----

    [Fact]
    public void FrameGrid_StartsAndIndexesAgree()
    {
        for (var frame = 0L; frame < 100; frame++)
        {
            Assert.Equal(frame, VideoMath.FrameAt(VideoMath.FrameStart(frame, 30), 30));
            Assert.Equal(frame, VideoMath.FrameAt(VideoMath.FrameStart(frame + 1, 30) - 1, 30));
        }
    }

    [Fact]
    public void GifFrameRate_IsCapped()
    {
        Assert.Equal(VideoMath.GifMaxFrameRate, VideoMath.FrameRateFor(VideoFormat.Gif, 60));
        Assert.Equal(60, VideoMath.FrameRateFor(VideoFormat.Mp4H264, 60));
        Assert.Equal(15, VideoMath.FrameRateFor(VideoFormat.Gif, 15));
    }

    [Fact]
    public void GifDelays_AddUpWithoutDrift()
    {
        var total = 0;
        for (var frame = 0; frame < 300; frame++)
        {
            var delay = VideoMath.GifDelay(VideoMath.FrameStart(frame, 30), VideoMath.FrameStart(frame + 1, 30));
            Assert.InRange(delay, 3, 4);
            total += delay;
        }
        Assert.Equal(1000, total); // 300 frames at 30 per second = 10 s
    }

    [Fact]
    public void GifDelay_IsNeverUnderTwo() => Assert.Equal(2, VideoMath.GifDelay(0, Second / 1000));

    [Fact]
    public void Clock_PausesAndGoesOn()
    {
        var now = 0L;
        var clock = new RecordingClock(() => now);
        now = 5 * Second;
        clock.Pause();
        now = 9 * Second;
        Assert.Equal(5 * Second, clock.Ticks);
        clock.Resume();
        now = 10 * Second;
        Assert.Equal(6 * Second, clock.Ticks);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(75, "01:15")]
    [InlineData(3725, "1:02:05")]
    public void Clock_Words(int seconds, string text) => Assert.Equal(text, VideoMath.Clock(TimeSpan.FromSeconds(seconds)));

    // ---- GIF palette ----

    private static byte[] Pixels(int width, int height, Func<int, int, (byte R, byte G, byte B)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (r, g, b) = colour(x, y);
            var at = (y * width + x) * 4;
            pixels[at] = b;
            pixels[at + 1] = g;
            pixels[at + 2] = r;
            pixels[at + 3] = 255;
        }
        return pixels;
    }

    [Fact]
    public void Palette_KeepsFewColoursExactly()
    {
        var colours = new (byte, byte, byte)[] { (255, 0, 0), (0, 128, 0), (0, 0, 255), (240, 240, 240) };
        var pixels = Pixels(8, 4, (x, y) => colours[(x + y) % 4]);
        var palette = new GifPalette();
        var indices = new byte[32];
        palette.Build(pixels, 8, 4, 32, 256, indices);
        Assert.Equal(2, palette.Bits);
        for (var i = 0; i < 32; i++)
        {
            var (r, g, b) = colours[(i % 8 + i / 8) % 4];
            var at = indices[i] * 3;
            Assert.Equal((r, g, b), (palette.Colours[at], palette.Colours[at + 1], palette.Colours[at + 2]));
        }
    }

    [Fact]
    public void Palette_HoldsToTheColourCount()
    {
        var pixels = Pixels(64, 64, (x, y) => ((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2)));
        var palette = new GifPalette();
        var indices = new byte[64 * 64];
        palette.Build(pixels, 64, 64, 64 * 4, VideoMath.GifColourCount(VideoQuality.Low), indices);
        Assert.Equal(6, palette.Bits);
        Assert.All(indices, i => Assert.True(i < 64));
    }

    [Fact]
    public void Gif_ReadsBackWithItsFramesColoursAndDelays()
    {
        var path = Path.Combine(Path.GetTempPath(), $"omnicontrol-test-{Guid.NewGuid():N}.gif");
        try
        {
            var red = Pixels(20, 10, (_, _) => (255, 0, 0));
            var split = Pixels(20, 10, (x, _) => x < 10 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255));
            using (var gif = new GifWriter(path, 20, 10, VideoQuality.High))
            {
                gif.WriteFrame(red, 80, 0);
                gif.WriteFrame(red, 80, Second / 10); // the same picture: the first frame lasts longer
                gif.WriteFrame(split, 80, Second / 5);
                gif.Finish(Second / 2);
            }
            using var codec = SKCodec.Create(path);
            Assert.Equal(2, codec.FrameCount);
            Assert.Equal(200, codec.FrameInfo[0].Duration);
            Assert.Equal(300, codec.FrameInfo[1].Duration);
            var info = new SKImageInfo(20, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(1)));
            Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(2, 5));
            Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(15, 5));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Gif_NoisyFrameReadsBackExactly()
    {
        // Random pixels of 64 colours fill the LZW code table several times, so every table reset is read back too.
        const int width = 160, height = 120;
        var colours = Enumerable.Range(0, 64).Select(i => ((byte)(i / 2 * 8), (byte)0, (byte)(i % 2 * 128))).ToArray();
        var random = new Random(7);
        var picks = Enumerable.Range(0, width * height).Select(_ => random.Next(colours.Length)).ToArray();
        var noisy = Pixels(width, height, (x, y) => colours[picks[y * width + x]]);
        var path = Path.Combine(Path.GetTempPath(), $"omnicontrol-test-{Guid.NewGuid():N}.gif");
        try
        {
            using (var gif = new GifWriter(path, width, height, VideoQuality.High))
            {
                gif.WriteFrame(noisy, width * 4, 0);
                gif.Finish(Second);
            }
            using var codec = SKCodec.Create(path);
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = colours[picks[y * width + x]];
                Assert.Equal(new SKColor(r, g, b), bitmap.GetPixel(x, y));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MakeOpaque_SetsEveryAlphaAndKeepsTheColours()
    {
        var pixels = Enumerable.Range(0, 4 * 37).Select(i => (byte)(i * 3)).ToArray(); // 37 pixels: whole vectors and a remainder
        var before = (byte[])pixels.Clone();
        ScreenImage.MakeOpaque(pixels);
        for (var i = 0; i < pixels.Length; i++) Assert.Equal(i % 4 == 3 ? 255 : before[i], pixels[i]);
    }

    [Fact]
    public void ClickCircle_IsPremultipliedInsideAndEmptyOutside()
    {
        const int radius = 3;
        var side = radius * 2 + 1;
        var pixels = new byte[side * side * 4];
        PointerPainter.Circle(pixels, radius, 0xFF8000, 0.5);
        Assert.Equal(new byte[] { 0, 64, 127, 127 }, pixels.AsSpan((radius * side + radius) * 4, 4).ToArray()); // the centre: B, G, R, A
        Assert.Equal(new byte[4], pixels.AsSpan(0, 4).ToArray()); // a corner is outside the circle
    }

    // ---- GIF worker queue ----

    /// <summary>A sink that records what reaches it and can hold the worker inside its first frame.</summary>
    private sealed class SlowSink : IVideoSink
    {
        public readonly List<(long At, byte First)> Frames = new();
        public readonly ManualResetEventSlim Entered = new(), Go = new();
        public long Finished = -1;
        public bool Fail;

        public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, long at)
        {
            if (Fail) throw new IOException("disk gone");
            Frames.Add((at, bgra[0]));
            Entered.Set();
            Go.Wait();
        }

        public long Length => Frames.Count;
        public long FramesDone => Frames.Count;
        public void Finish(long end) => Finished = end;
        public void Dispose() { }
    }

    private static byte[] Frame(byte value) => Enumerable.Repeat(value, 2 * 2 * 4).ToArray();

    [Fact]
    public void Queue_KeepsOneFrameWaitingAndDropsTheRest()
    {
        var inner = new SlowSink();
        using var queue = new QueuedVideoSink(inner, 2, 2, "test");
        queue.WriteFrame(Frame(1), 8, 10);
        Assert.True(inner.Entered.Wait(5000)); // the worker is busy with frame 1
        queue.WriteFrame(Frame(2), 8, 20); // waits
        queue.WriteFrame(Frame(3), 8, 30); // dropped: frame 2 lasts longer
        inner.Go.Set();
        queue.Finish(40);
        Assert.Equal(new[] { (10L, (byte)1), (20L, (byte)2) }, inner.Frames);
        Assert.Equal(40, inner.Finished);
        Assert.Equal(2, queue.Length);
    }

    [Fact]
    public void Queue_HandsAWorkerFailureToTheRecordingThread()
    {
        var inner = new SlowSink { Fail = true };
        using var queue = new QueuedVideoSink(inner, 2, 2, "test");
        queue.WriteFrame(Frame(1), 8, 10);
        Assert.Throws<IOException>(() => queue.Finish(20));
        Assert.Equal(-1, inner.Finished);
    }

    // ---- Drawing sizes ----

    [Fact]
    public void ToolSizes_StepThroughTheListAndRemember()
    {
        var store = new Dictionary<string, double>();
        var saves = 0;
        var sizes = new ToolSizes(store, () => saves++);
        Assert.Equal(MarkLook.TextSize, sizes.Of(DrawTool.Text));
        Assert.True(sizes.Step(DrawTool.Text, 1));
        Assert.Equal(24, sizes.Of(DrawTool.Text));
        Assert.Equal(MarkLook.Stroke, sizes.Of(DrawTool.Pen)); // per tool
        Assert.Equal(1, saves);
        Assert.Equal(24, new ToolSizes(store, () => { }).Of(DrawTool.Text));
    }

    [Fact]
    public void ToolSizes_HoldAtTheEnds()
    {
        var sizes = new ToolSizes(new Dictionary<string, double>(), () => { });
        while (sizes.Step(DrawTool.Line, -1)) { }
        Assert.Equal(MarkLook.StrokeSizes[0], sizes.Of(DrawTool.Line));
        Assert.False(sizes.Step(DrawTool.Line, -1));
        Assert.False(sizes.Step(DrawTool.Blur, 1));
        Assert.False(ToolSizes.Has(DrawTool.Blur));
    }
}
