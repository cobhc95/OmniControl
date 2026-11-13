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
    /// A touchpad frame arriving this long after the previous one starts a new touch: a lost "fingers lifted" report never leaves a
    /// gesture waiting (a precision touchpad reports at least every 10-20 ms while fingers are down).
    /// </summary>
    public const int TrackpadTouchGapMs = 250;

    /// <summary>How long stopping the touchpad watcher waits for its thread to unregister and close (it calls nothing slow).</summary>
    public const int TrackpadStopWaitMs = 1000;

    /// <summary>How long an elevated helper (the APO installer) may run before OmniControl stops waiting for it.</summary>
    public const int ElevatedHelperTimeoutMs = 120_000;
}
