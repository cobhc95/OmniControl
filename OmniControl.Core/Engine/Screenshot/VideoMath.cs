namespace OmniControl.Core.Engine.Screenshot;

/// <summary>What a recording is saved as (stored by number: never reorder).</summary>
public enum VideoFormat { Mp4H264, Mp4Hevc, Gif }

/// <summary>How good a recording looks, and so how large it is (stored by number: never reorder).</summary>
public enum VideoQuality { Low, Medium, High }

/// <summary>
/// The one table of recording formats (names, file endings, which take sound and an encoder choice) and the pure maths of a recording:
/// the even frame size H.264 and HEVC need, the bitrate from size, frame rate and quality, the frame grid, a GIF's frame delays and colours.
/// Times are in 100-nanosecond units (Media Foundation's unit), as are all times of the recorder.
/// </summary>
public static class VideoMath
{
    public const long TicksPerSecond = 10_000_000;

    /// <summary>The frame rates offered; <see cref="DefaultFrameRate"/> is the default.</summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 15, 24, 30, 60 };

    public const int DefaultFrameRate = 30;

    /// <summary>A GIF holds its frame delays in hundredths of a second and browsers slow down anything under two, so a GIF never goes faster than this.</summary>
    public const int GifMaxFrameRate = 30;

    /// <summary>Bits per pixel per frame for H.264 at Low, Medium and High; HEVC needs <see cref="HevcShare"/> of that for the same look.</summary>
    private static readonly double[] BitsPerPixel = { 0.04, 0.08, 0.15 };

    private const double HevcShare = 0.6;

    /// <summary>The bitrate is held between these (bits per second): tiny areas still look clean, huge ones stay writable.</summary>
    public const int MinBitrate = 500_000, MaxBitrate = 100_000_000;

    /// <summary>Colours in each GIF frame at Low, Medium and High.</summary>
    private static readonly int[] GifColours = { 64, 128, 256 };

    public static string Name(VideoFormat format) => format switch
    {
        VideoFormat.Mp4Hevc => "MP4 (HEVC)",
        VideoFormat.Gif => "GIF",
        _ => "MP4 (H.264)",
    };

    public static string Extension(VideoFormat format) => format == VideoFormat.Gif ? ".gif" : ".mp4";

    /// <summary>Every format, in the settings' order.</summary>
    public static readonly IReadOnlyList<VideoFormat> Formats = new[] { VideoFormat.Mp4H264, VideoFormat.Mp4Hevc, VideoFormat.Gif };

    public static VideoFormat Parse(string name) => Formats.FirstOrDefault(f => Name(f) == name, VideoFormat.Mp4H264);

    /// <summary>MP4 records sound and can use the graphics card; a GIF has neither.</summary>
    public static bool IsMp4(VideoFormat format) => format != VideoFormat.Gif;

    /// <summary>A side rounded down to an even number (at least 2): H.264 and HEVC encode only even frame sizes.</summary>
    public static int Even(int side) => Math.Max(2, side & ~1);

    /// <summary>The area a recording really covers: for MP4 its width and height rounded down to even numbers, for a GIF the area as it is.</summary>
    public static ScreenRect FrameArea(ScreenRect area, VideoFormat format) =>
        IsMp4(format) ? area with { Width = Even(area.Width), Height = Even(area.Height) } : area;

    /// <summary>
    /// An MP4 never goes faster than this: H.264 level 5.2 (the highest many hardware encoders take) holds 4K only up to 60 frames a second,
    /// so a stored rate above it (a 140 Hz monitor's) is held here rather than refused by the encoder.
    /// </summary>
    public const int Mp4MaxFrameRate = 60;

    /// <summary>The frame rate a format really records at (a GIF is held to <see cref="GifMaxFrameRate"/>, an MP4 to <see cref="Mp4MaxFrameRate"/>).</summary>
    public static int FrameRateFor(VideoFormat format, int frameRate) =>
        Math.Clamp(frameRate, 1, format == VideoFormat.Gif ? GifMaxFrameRate : Mp4MaxFrameRate);

    /// <summary>The video bitrate in bits per second: pixels per second times the quality's bits per pixel, less for HEVC, held to the limits.</summary>
    public static int Bitrate(int width, int height, int frameRate, VideoQuality quality, VideoFormat format)
    {
        var bits = (double)width * height * frameRate * BitsPerPixel[(int)quality];
        if (format == VideoFormat.Mp4Hevc) bits *= HevcShare;
        return (int)Math.Clamp(Math.Round(bits), MinBitrate, MaxBitrate);
    }

    /// <summary>How many colours each GIF frame may have at this quality.</summary>
    public static int GifColourCount(VideoQuality quality) => GifColours[(int)quality];

    /// <summary>The length of one frame.</summary>
    public static long FrameTicks(int frameRate) => TicksPerSecond / frameRate;

    /// <summary>Which frame of the grid a moment falls in (frame n covers [n, n+1) frame lengths).</summary>
    public static long FrameAt(long ticks, int frameRate) => ticks * frameRate / TicksPerSecond;

    /// <summary>When frame n starts, rounded up to a whole tick so the moment lies inside frame n (rounding down put it in the frame before).</summary>
    public static long FrameStart(long frame, int frameRate) => (frame * TicksPerSecond + frameRate - 1) / frameRate;

    /// <summary>
    /// A GIF frame's delay in hundredths of a second, shown from <paramref name="start"/> until <paramref name="next"/>. Both moments are
    /// rounded to the hundredth first, so the delays of a whole GIF add up to its real length with no drift. At least 2 (browsers slow
    /// down a delay of 0 or 1).
    /// </summary>
    public static int GifDelay(long start, long next) => (int)Math.Max(2, Centis(next) - Centis(start));

    private static long Centis(long ticks) => (ticks + TicksPerSecond / 200) / (TicksPerSecond / 100);

    /// <summary>mm:ss, or h:mm:ss from an hour on: the recording bar's timer.</summary>
    public static string Clock(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
}
