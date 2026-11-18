namespace OmniControl.Core;

/// <summary>Waits and limits inside Core, in one place (the app's own intervals are in App/Services/Timing.cs; Core cannot reference App).</summary>
public static class CoreTiming
{
    /// <summary>Least gap between two DDC/CI writes to one monitor; many EDIDs dislike bursts closer than 30 ms.</summary>
    public const int DdcMinSetIntervalMs = 40;

    /// <summary>Clipboard open retries and the wait between them (another program may hold the clipboard for a moment).</summary>
    public const int ClipboardAttempts = 10, ClipboardWaitMs = 20;

    /// <summary>How long the pointer-area probe waits for a window to answer a hit test before giving up on it.</summary>
    public const uint HitTestTimeoutMs = 50;

    /// <summary>
    /// An app whose "is it a link, tab or button?" lookup took longer than <see cref="AutoscrollSlowMs"/> is not asked again for
    /// <see cref="AutoscrollBackoffMs"/> (presses there pass meanwhile), so a busy app is never kept busier; the watcher remembers whether
    /// a process is Windows' shell for at most <see cref="AutoscrollProcessCache"/> processes.
    /// </summary>
    public const int AutoscrollSlowMs = 200, AutoscrollBackoffMs = 5000, AutoscrollProcessCache = 256;

    /// <summary>
    /// The autoscroll answer is looked up by a worker (<c>AutoscrollWatcher</c>) every <see cref="AutoscrollWatchMs"/> while the pointer is
    /// somewhere the last answer does not cover; an answer covers the same window within <see cref="AutoscrollCachePx"/> pixels for
    /// <see cref="AutoscrollCacheMs"/>. The mouse hook only reads it.
    /// </summary>
    public const int AutoscrollWatchMs = 60, AutoscrollCachePx = 8, AutoscrollCacheMs = 1500;

    /// <summary>How many parents above the element under the pointer the link lookup also checks.</summary>
    public const int AutoscrollParentLevels = 4;

    /// <summary>
    /// How often a worker looks whether a full-screen app is in front (<c>FullScreenWatcher</c>) and which program the user is working in
    /// (<c>ForegroundProgram</c>); the mouse hook only reads what they found.
    /// </summary>
    public const int FullScreenWatchMs = 500, ForegroundWatchMs = 100;

    /// <summary>
    /// The size and place Windows' own volume and brightness pop-up may have (<c>WindowsOsdWindows.HasFlyoutShape</c>), as shares of its
    /// monitor: at most this wide and tall, its centre this close to the monitor's centre line, its edge this close to the monitor's bottom
    /// or top; and at least <see cref="WindowsOsdMinSidePx"/> on each side. A screen-sized Explorer host or a thumbnail preview fails.
    /// </summary>
    public const double WindowsOsdMaxWidthShare = 0.4, WindowsOsdMaxHeightShare = 0.25, WindowsOsdCentreShare = 0.1, WindowsOsdEdgeShare = 0.3;

    /// <inheritdoc cref="WindowsOsdMaxWidthShare"/>
    public const int WindowsOsdMinSidePx = 20;

    /// <summary>
    /// A call into Explorer (its touchpad settings service) is waited for at most <see cref="ShellCallTimeoutMs"/>; one slower than
    /// <see cref="ShellCallSlowMs"/> is logged, naming the call.
    /// </summary>
    public const int ShellCallTimeoutMs = 1500, ShellCallSlowMs = 200;

    /// <summary>At start-up the hardware listeners' hidden window and SystemEvents are each waited for at most this long (the UI thread waits).</summary>
    public const int HardwareSignalsStartWaitMs = 3000;

    /// <summary>A mouse-hook call slower than this is reported (Windows holds all mouse input while it runs, and drops a hook that takes too long).</summary>
    public const int HookSlowMs = 50;

    /// <summary>
    /// A hook problem (a slow call, the hook dropped by Windows) goes to the errors log at most once per this time, with a count of the ones
    /// since the last line, so a stalled hook never floods the log.
    /// </summary>
    public const int HookReportGapMs = 60_000;

    /// <summary>
    /// The mouse hook's watchdog looks this often whether Windows has silently dropped the hook (pointer moved and input happened, yet
    /// the hook saw nothing), and puts it back when it has.
    /// </summary>
    public const int HookWatchdogMs = 3000;

    /// <summary>Most diagnostic lines the mouse hook writes per run.</summary>
    public const int HookLogLimit = 150;

    /// <summary>
    /// Effects pipe (capture, effects, playback): how much sound each capture packet and the playback buffer hold, how much is
    /// gathered before playback starts, and the most that may wait in between (more is dropped so the delay never creeps up when
    /// the two clocks drift). Smaller = less delay but more risk of crackles on a busy PC.
    /// </summary>
    public const int PipeCaptureBufferMs = 20, PipeRenderLatencyMs = 30, PipePrefillMs = 20, PipeMaxQueueMs = 120;

    /// <summary>How long stopping a display-only loopback tap waits for its thread to end.</summary>
    public const int DisplayTapStopWaitMs = 500;

    /// <summary>The effects pipe's sound format: every capture is asked for this and Windows converts the program's own format.</summary>
    public const int PipeSampleRate = 48000, PipeChannels = 2;

    /// <summary>Samples (all channels) in one millisecond of the pipe's format.</summary>
    public const int PipeSamplesPerMs = PipeSampleRate * PipeChannels / 1000;

    /// <summary>Size of every ring between a capture or the first device and a player: four times the most that may wait, so a burst never overwrites.</summary>
    public const int PipeRingSamples = PipeSamplesPerMs * PipeMaxQueueMs * 4;

    /// <summary>
    /// A VST3 plug-in the audio thread has not run for this long counts as idle: setting changes made meanwhile are handed to its
    /// sound part by OmniControl (one silent frame) before its settings are saved, so a plug-in that keeps its values there saves them.
    /// </summary>
    public const int PluginIdleMs = 500;

    /// <summary>Most frames the effects chain processes in one go (the playback thread splits larger requests).</summary>
    public const int PipeMaxBlockFrames = 1024;

    /// <summary>
    /// An extra playback device of a pipe (several outputs): the queue it aims to keep, how far one ms above or below that nudges its
    /// reading rate, the largest nudge (0.5 %, far beyond real clock drift, inaudible as pitch), and how quickly the measured queue
    /// follows the real one per block (smooths out the block-sized steps).
    /// </summary>
    public const int PipeDriftTargetMs = 40;
    public const double PipeDriftShiftPerMs = 0.0002, PipeDriftMaxShift = 0.005, PipeDriftSmoothing = 0.05;

    /// <summary>
    /// The queue a capture of a pipe aims to keep (each capture runs on its own clock and is read through the same drift correction
    /// as an extra device): the prefill, so a capture waits no longer than it did before drift correction.
    /// </summary>
    public const int PipeCaptureDriftTargetMs = PipePrefillMs;

    /// <summary>
    /// A touchpad frame arriving this long after the previous one starts a new touch: a lost "fingers lifted" report never leaves a
    /// gesture waiting (a precision touchpad reports at least every 10-20 ms while fingers are down).
    /// </summary>
    public const int TrackpadTouchGapMs = 250;

    /// <summary>
    /// A tap on the touchpad: every finger lifts within <see cref="TrackpadTapMaxMs"/> of the first touching, the fingers of one tap land
    /// within <see cref="TrackpadTapLandMs"/> of each other, and a second tap of the same finger count within
    /// <see cref="TrackpadDoubleTapMs"/> of the first is a double tap.
    /// </summary>
    public const int TrackpadTapMaxMs = 250, TrackpadTapLandMs = 80, TrackpadDoubleTapMs = 400;

    /// <summary>How long stopping the touchpad watcher waits for its thread to unregister and close (it calls nothing slow).</summary>
    public const int TrackpadStopWaitMs = 1000;

    /// <summary>
    /// Hardware announcements come from two listeners (the hidden window and .NET's <c>SystemEvents</c>), so the same kind seen again within
    /// this time is the same event told twice and is dropped (<c>HardwareSignalFilter</c>).
    /// </summary>
    public const int HardwareSignalDuplicateMs = 1000;

    /// <summary>How long one read of the System event log (the power journal's sleeps and wakes, <c>PowerEventLog</c>) may wait for a batch of records.</summary>
    public const int EventLogReadWaitMs = 2000;

    /// <summary>
    /// The takeover's safety net (<c>ShellSurfaceGuard</c>): after a gesture whose Windows action is supposed to be off, a panel that
    /// Windows' action opens and that comes to the front within <see cref="TrackpadSurfaceWatchMs"/> is closed with Esc; at most
    /// <see cref="TrackpadSurfaceDismissLimit"/> are closed per minute, so a mistake can never turn into a stream of key presses.
    /// </summary>
    public const int TrackpadSurfaceWatchMs = 700, TrackpadSurfaceDismissLimit = 20;

    /// <summary>
    /// While OmniControl keeps a Windows touchpad gesture off, it turns it off again every time something turns it back on, never giving
    /// up, but at most once per <see cref="TrackpadTakeoverReapplyGapMs"/> (a program that keeps writing it back cannot start a tight
    /// loop; the next look turns it off again). The log keeps at most <see cref="TrackpadTakeoverReapplyLogLimit"/> of those lines per
    /// gesture within <see cref="TrackpadTakeoverReapplyLogWindowMs"/>.
    /// </summary>
    public const int TrackpadTakeoverReapplyGapMs = 1000, TrackpadTakeoverReapplyLogLimit = 5, TrackpadTakeoverReapplyLogWindowMs = 60_000;

    /// <summary>How long an elevated helper (the APO installer) may run before OmniControl stops waiting for it.</summary>
    public const int ElevatedHelperTimeoutMs = 120_000;

    /// <summary>
    /// How long OmniControl waits for Equalizer APO's Configurator (Device Selector) to be closed before it reads the devices again: the
    /// user ticks devices there at their own pace, so the wait is long; a worker waits, never the UI.
    /// </summary>
    public const int ConfiguratorWaitMs = 30 * 60_000;

    /// <summary>How long a Tailscale or netsh read may take, and how long a plain connect test waits (Other PCs over Tailscale).</summary>
    public const int TailscaleCliWaitMs = 5_000, TcpReachWaitMs = 3_000;

    /// <summary>One adb command (connect, a screen dump, a tap) is stopped after this long; a swipe takes this long on the screen.</summary>
    public const int AdbCommandTimeoutMs = 20_000, AdbSwipeMs = 300;

    /// <summary>A touch that moves less than this many pixels (of the screen) is a tap, not a swipe, when a recording reads the screen's touches.</summary>
    public const int AdbTapSlopPx = 24;

    /// <summary>
    /// A screenshot is blank when every pixel of a <see cref="ScreenBlankGrid"/> by <see cref="ScreenBlankGrid"/> sample is within this many levels
    /// (0-255) of the first one; it has the wrong size when its shape differs from the screen's by more than this share.
    /// </summary>
    public const int ScreenBlankGrid = 16, ScreenBlankTolerance = 6;
    public const double ScreenShapeTolerance = 0.05;

    /// <summary>
    /// Reading a simulated device's state (<c>Engine/Adb/StateModel</c>): a reading whose weighted vote (0-1) reaches the threshold is On or Off,
    /// below it Unknown; recent readings are smoothed by the factor and, below the warning level, the device is said to want teaching again.
    /// </summary>
    public const double StateReadThreshold = 0.5, StateDriftWarn = 0.65, StateDriftSmoothing = 0.3;

    /// <summary>How much each family of signals counts in a reading (attributes, words, icons, colour, shape).</summary>
    public const double StateWeightAttributes = 1.0, StateWeightWords = 0.9, StateWeightViews = 0.6, StateWeightColour = 0.9, StateWeightShape = 0.6;

    /// <summary>
    /// How far apart on and off must be for a measure to count: on the hue-saturation disc (0-2), as a share of coloured pixels (0-1), and as
    /// the share of the shape's 64 bits that differ.
    /// </summary>
    public const double StateMinColourSeparation = 0.12, StateMinShareSeparation = 0.1, StateMinShapeSeparation = 0.12;

    /// <summary>
    /// Colour measures: the most points sampled per region; below this saturation or brightness a pixel counts as grey; the least share of
    /// coloured pixels whose hue counts as the region's dominant colour.
    /// </summary>
    public const int StateSampleBudget = 4096;
    public const double StateGreySaturation = 0.2, StateDarkValue = 0.15, StateDominantMinShare = 0.1;

    /// <summary>How far (a share of the screen's width) a found switch may sit from where it was taught relative to its card's name.</summary>
    public const double StateAnchorSlack = 0.15;

    /// <summary>
    /// Screen recording sound: each capture's buffer; the sound is written this far behind the picture's clock (so a capture packet has
    /// arrived before its time is written); when more than the max waits, only the lag's worth is kept, so the sound never drifts.
    /// </summary>
    public const int RecordSoundBufferMs = 2000, RecordSoundLagMs = 100, RecordSoundMaxQueueMs = 300;

    /// <summary>Screen recording: sound is written in pieces of at least this length; the size, length and disk-space limits are checked this often.</summary>
    public const int RecordSoundChunkMs = 20, RecordLimitCheckMs = 1000;

    /// <summary>Screen recording on the graphics card: when Windows takes the Desktop Duplication away, it is asked for again this often (GDI copies meanwhile).</summary>
    public const int RecordDuplicationRetryMs = 500;

    /// <summary>
    /// Screen recording's watchdog (<see cref="Engine.Screenshot.ScreenRecorder"/>): a way of recording (encoder and screen copy) must open
    /// within <see cref="RecordOpenWaitMs"/> and have its first frame encoded within <see cref="RecordFirstFrameWaitMs"/> of recording time,
    /// no single call into the encoder may take longer than <see cref="RecordStuckMs"/>, and finishing the file after Stop may take
    /// <see cref="RecordFinishWaitMs"/>; past any of these the next way is tried (or, while finishing, the recording ends as failed). The
    /// watchdog looks every <see cref="RecordWatchMs"/>; the encoder's counts are read every <see cref="RecordStatsMs"/> and logged every
    /// <see cref="RecordLogFramesMs"/>; the whole start (adapters, sound, every way tried) gives up after <see cref="RecordStartWaitMs"/>.
    /// </summary>
    public const int RecordOpenWaitMs = 6000, RecordFirstFrameWaitMs = 4000, RecordStuckMs = 5000, RecordFinishWaitMs = 10000,
        RecordWatchMs = 100, RecordStatsMs = 500, RecordLogFramesMs = 5000, RecordStartWaitMs = 45000;

    /// <summary>How long Windows' shutdown.exe may take to accept or refuse a shut down, restart or sign out; still running then counts as accepted.</summary>
    public const int ShutdownExeWaitMs = 5000;

    /// <summary>
    /// Reading how this PC can be woken (<c>WakeReader</c>): each source gets its own wait, and a slow one is given up on so the next is tried
    /// (WMI can take 18 s on a broken adapter; powercfg answers in well under a second).
    /// </summary>
    public const int WakeReadWmiMs = 5_000, WakeReadPowercfgMs = 4_000, WakeReadApiMs = 3_000;
}
