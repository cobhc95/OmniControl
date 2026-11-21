namespace OmniControl.App.Shared.Services;

// The intervals and limits of the OSD pop-up and Windows' own pop-up; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How long the OSD pop-up stays visible.
    public static readonly TimeSpan OsdVisible = TimeSpan.FromMilliseconds(1500);

    /// How often the hidden pop-up draws one frame off screen, so its render thread, graphics device and memory do not go cold
    /// between pop-ups (a cold pipeline was the delay before a pop-up appeared); often enough for that, rare enough to cost nothing while idle.
    public static readonly TimeSpan OsdKeepWarm = TimeSpan.FromSeconds(30);

    /// Changes made by OmniControl itself within this window don't pop the OSD a second time.
    public static readonly TimeSpan OsdOwnChangeGrace = TimeSpan.FromSeconds(1);

    /// How often the laptop brightness is read as a fallback when Windows' brightness event cannot be listened to (each read is a WMI query).
    public static readonly TimeSpan OsdBrightnessPoll = TimeSpan.FromSeconds(2);

    /// While the brightness event is listened to but not yet seen working, the brightness is also read this often, for this long after
    /// the watch starts, then the event alone is trusted.
    public static readonly TimeSpan OsdBrightnessCheckPoll = TimeSpan.FromSeconds(3), OsdBrightnessCheckFor = TimeSpan.FromMinutes(1);

    /// Delay before the first of those reads, so it does not add to the start-up work.
    public static readonly TimeSpan OsdBrightnessFirstPoll = TimeSpan.FromMilliseconds(500);

    /// OSD placement, in device-independent pixels (a place's offsets are stored in them, so a place survives a change of resolution or
    /// scale): the gap the seven built-in places keep from the screen edge; how close a dragged OSD must come to an edge, a margin line or
    /// the centre lines to snap to it; how far an arrow key (or Shift + arrow) nudges it; and the most an offset may be.
    public const int OsdMargin = 32, OsdSnap = 8, OsdNudge = 1, OsdNudgeLarge = 10, OsdOffsetMax = 1200;

    /// Whole pixels added to a measured pop-up's size so rounding never puts it a pixel over the margin.
    public const int OsdRoundingSlack = 2;

    /// A play/pause change this soon after a track change is part of the skip, so only the track is shown.
    public static readonly TimeSpan OsdTrackQuiet = TimeSpan.FromMilliseconds(1200);

    /// How often Windows' own OSD window is looked for so it can be hidden (only within <see cref="WindowsOsdMatch"/> after an outside change).
    public static readonly TimeSpan WindowsOsdPoll = TimeSpan.FromMilliseconds(60);

    /// An Explorer pop-up is taken for Windows' own volume pop-up (and hidden) only when it opens within this time AFTER a volume,
    /// brightness, microphone or media change made outside OmniControl (0 ms or more, never before); any other Explorer pop-up is never touched.
    public static readonly TimeSpan WindowsOsdMatch = TimeSpan.FromSeconds(1);

    /// Kill switch: when more than this many different windows have been taken for Windows' pop-up in one run, hiding stops for the rest of
    /// the run (Explorer restarting makes a new one; more than this means the windows are not being told apart).
    public const int WindowsOsdMaxWindows = 3;
}
