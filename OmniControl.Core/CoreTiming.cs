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

    /// <summary>A mouse-hook handler slower than this is reported (Windows drops a hook that takes too long).</summary>
    public const int HookSlowMs = 50;

    /// <summary>Most diagnostic lines the mouse hook writes per run.</summary>
    public const int HookLogLimit = 150;

    /// <summary>
    /// Effects pipe (capture, effects, playback): how much sound each capture packet and the playback buffer hold, how much is
    /// gathered before playback starts, and the most that may wait in between (more is dropped so the delay never creeps up when
    /// the two clocks drift). Smaller = less delay but more risk of crackles on a busy PC.
    /// </summary>
    public const int PipeCaptureBufferMs = 20, PipeRenderLatencyMs = 30, PipePrefillMs = 20, PipeMaxQueueMs = 120;

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

    /// <summary>One adb command (connect, a screen dump, a tap) is stopped after this long; a swipe takes this long on the screen.</summary>
    public const int AdbCommandTimeoutMs = 20_000, AdbSwipeMs = 300;

    /// <summary>A touch that moves less than this many pixels (of the screen) is a tap, not a swipe, when a recording reads the screen's touches.</summary>
    public const int AdbTapSlopPx = 24;
}
