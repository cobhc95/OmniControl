namespace OmniControl.Core.Engine.Screenshot;

/// What a recording is saved as (stored by number: never reorder).
public enum VideoFormat { Mp4H264, Mp4Hevc, Gif }

/// How good a recording looks, and so how large it is (stored by number: never reorder).
public enum VideoQuality { Low, Medium, High }

/// The one table of recording formats (names, file endings, which take sound and an encoder choice) and the pure maths of a recording:
/// the even frame size H.264 and HEVC need, the bitrate from size, frame rate and quality, the frame grid, a GIF's frame delays and colours.
/// Times are in 100-nanosecond units (Media Foundation's unit), as are all times of the recorder.
public static class VideoMath
{
    public const long TicksPerSecond = 10_000_000;

    /// The frame rates offered; <see cref="DefaultFrameRate"/> is the default.
    public static readonly IReadOnlyList<int> FrameRates = new[] { 15, 24, 30, 60 };

    public const int DefaultFrameRate = 30;

    /// A GIF holds its frame delays in hundredths of a second and browsers slow down anything under two, so a GIF never goes faster than this.
    public const int GifMaxFrameRate = 30;

    /// Bits per pixel per frame for H.264 at Low, Medium and High; HEVC needs <see cref="HevcShare"/> of that for the same look.
    private static readonly double[] BitsPerPixel = { 0.04, 0.08, 0.15 };

    private const double HevcShare = 0.6;

    /// The bitrate is held between these (bits per second): tiny areas still look clean, huge ones stay writable.
    public const int MinBitrate = 500_000, MaxBitrate = 100_000_000;

    /// Colours in each GIF frame at Low, Medium and High.
    private static readonly int[] GifColours = { 64, 128, 256 };

    public static string Name(VideoFormat format) => format switch
    {
        VideoFormat.Mp4Hevc => "MP4 (HEVC)",
        VideoFormat.Gif => "GIF",
        _ => "MP4 (H.264)",
    };

    public static string Extension(VideoFormat format) => format == VideoFormat.Gif ? ".gif" : ".mp4";

    /// Every format, in the settings' order.
    public static readonly IReadOnlyList<VideoFormat> Formats = new[] { VideoFormat.Mp4H264, VideoFormat.Mp4Hevc, VideoFormat.Gif };

    public static VideoFormat Parse(string name) => Formats.FirstOrDefault(f => Name(f) == name, VideoFormat.Mp4H264);

    /// MP4 records sound and can use the graphics card; a GIF has neither.
    public static bool IsMp4(VideoFormat format) => format != VideoFormat.Gif;

    /// A side rounded down to an even number (at least 2): H.264 and HEVC encode only even frame sizes.
    public static int Even(int side) => Math.Max(2, side & ~1);

    /// The area a recording really covers: for MP4 its width and height rounded down to even numbers, for a GIF the area as it is.
    public static ScreenRect FrameArea(ScreenRect area, VideoFormat format) =>
        IsMp4(format) ? area with { Width = Even(area.Width), Height = Even(area.Height) } : area;

    /// An MP4 never goes faster than this: H.264 level 5.2 (the highest many hardware encoders take) holds 4K only up to 60 frames a second,
    /// so a stored rate above it (a 140 Hz monitor's) is held here rather than refused by the encoder.
    public const int Mp4MaxFrameRate = 60;

    /// The frame rate a format really records at (a GIF is held to <see cref="GifMaxFrameRate"/>, an MP4 to <see cref="Mp4MaxFrameRate"/>).
    public static int FrameRateFor(VideoFormat format, int frameRate) =>
        Math.Clamp(frameRate, 1, format == VideoFormat.Gif ? GifMaxFrameRate : Mp4MaxFrameRate);

    /// The video bitrate in bits per second: pixels per second times the quality's bits per pixel, less for HEVC, held to the limits.
    public static int Bitrate(int width, int height, int frameRate, VideoQuality quality, VideoFormat format)
    {
        var bits = (double)width * height * frameRate * BitsPerPixel[(int)quality];
        if (format == VideoFormat.Mp4Hevc) bits *= HevcShare;
        return (int)Math.Clamp(Math.Round(bits), MinBitrate, MaxBitrate);
    }

    /// How many colours each GIF frame may have at this quality.
    public static int GifColourCount(VideoQuality quality) => GifColours[(int)quality];

    /// The length of one frame.
    public static long FrameTicks(int frameRate) => TicksPerSecond / frameRate;

    /// Which frame of the grid a moment falls in (frame n covers [n, n+1) frame lengths).
    public static long FrameAt(long ticks, int frameRate) => ticks * frameRate / TicksPerSecond;

    /// When frame n starts, rounded up to a whole tick so the moment lies inside frame n (rounding down put it in the frame before).
    public static long FrameStart(long frame, int frameRate) => (frame * TicksPerSecond + frameRate - 1) / frameRate;

    /// A GIF frame's delay in hundredths of a second, shown from <paramref name="start"/> until <paramref name="next"/>. Both moments are
    /// rounded to the hundredth first, so the delays of a whole GIF add up to its real length with no drift. At least 2 (browsers slow
    /// down a delay of 0 or 1).
    public static int GifDelay(long start, long next) => (int)Math.Max(2, Centis(next) - Centis(start));

    private static long Centis(long ticks) => (ticks + TicksPerSecond / 200) / (TicksPerSecond / 100);

    /// mm:ss, or h:mm:ss from an hour on: the recording bar's timer.
    public static string Clock(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
}
