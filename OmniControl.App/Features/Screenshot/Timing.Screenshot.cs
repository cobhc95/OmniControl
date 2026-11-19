namespace OmniControl.App.Shared.Services;

// The intervals and limits of screenshots and screen recording; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// While the screenshot overlay is open in its strong "Stay on top" mode it puts itself back on top (and takes the foreground back) this often.
    public static readonly TimeSpan ScreenshotOnTopRefresh = TimeSpan.FromMilliseconds(250);

    /// After Stop is asked, a recording that has not finished its file within this long counts as stuck: it is said once, and the bar's X (Esc twice, the emergency key) then offers "Force close". Esc pressed twice within <see cref="RecordingEscTwice"/> asks to discard.
    public static readonly TimeSpan RecordingStopWait = TimeSpan.FromSeconds(8), RecordingEscTwice = TimeSpan.FromMilliseconds(700);

    /// The recording bar's timer is updated this often.
    public static readonly TimeSpan RecordingTimerTick = TimeSpan.FromMilliseconds(250);

    /// A screen recording stops by itself at this length (an MP4, a GIF), at this file size (an MP4, a GIF), or when the disk has less than this free.
    public static readonly TimeSpan VideoMaxLength = TimeSpan.FromHours(4), GifMaxLength = TimeSpan.FromMinutes(2);

    public const long VideoMaxBytes = 16L << 30, GifMaxBytes = 500L << 20, VideoMinFreeBytes = 1L << 30;
}
