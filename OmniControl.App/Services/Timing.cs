namespace OmniControl.App.Services;

/// <summary>Every interval and grace period the app uses, in one place, so a timing change never means a hunt through the code.</summary>
public static class Timing
{
    /// <summary>How often the mixer re-reads programs and devices from Windows.</summary>
    public static readonly TimeSpan MixerRefresh = TimeSpan.FromSeconds(2);

    /// <summary>After the user changes a program's volume or device, ignore what Windows reports for this long.</summary>
    public static readonly TimeSpan MixerUserChangeGrace = TimeSpan.FromSeconds(3);

    /// <summary>After the user moves a master or input slider, don't overwrite it with the system value for this long.</summary>
    public static readonly TimeSpan SliderEchoGrace = TimeSpan.FromSeconds(2);

    /// <summary>Refresh rate of the Volume page's level meters (25 per second).</summary>
    public static readonly TimeSpan MeterFrame = TimeSpan.FromMilliseconds(40);

    /// <summary>How long the OSD pop-up stays visible.</summary>
    public static readonly TimeSpan OsdVisible = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// How often the hidden pop-up draws one frame off screen, so its render thread, graphics device and memory never go cold
    /// between pop-ups (a cold pipeline was the delay before a pop-up appeared).
    /// </summary>
    public static readonly TimeSpan OsdKeepWarm = TimeSpan.FromSeconds(1);

    /// <summary>After start-up, wait this long (pages built, pop-up warm-up drawn) before keeping the memory in use resident.</summary>
    public static readonly TimeSpan KeepResidentAfterStart = TimeSpan.FromSeconds(5);

    /// <summary>Changes made by OmniControl itself within this window don't pop the OSD a second time.</summary>
    public static readonly TimeSpan OsdOwnChangeGrace = TimeSpan.FromSeconds(1);

    /// <summary>How often the laptop brightness is read as a fallback to the WMI event, until the event is seen to work (each read is a WMI query).</summary>
    public static readonly TimeSpan OsdBrightnessPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>Delay before the first of those reads, so it does not add to the start-up work.</summary>
    public static readonly TimeSpan OsdBrightnessFirstPoll = TimeSpan.FromMilliseconds(500);

    /// <summary>After a USB plug or unplug, wait this long for the rest of the device's parts to appear before showing the OSD once.</summary>
    public static readonly TimeSpan OsdUsbSettle = TimeSpan.FromMilliseconds(800);

    /// <summary>A play/pause change this soon after a track change is part of the skip, so only the track is shown.</summary>
    public static readonly TimeSpan OsdTrackQuiet = TimeSpan.FromMilliseconds(1200);

    /// <summary>How often Windows' own OSD window is looked for so it can be hidden.</summary>
    public static readonly TimeSpan WindowsOsdPoll = TimeSpan.FromMilliseconds(60);

    /// <summary>Minimum gap between attempts to create the hiding helper for Windows' OSD.</summary>
    public static readonly TimeSpan WindowsOsdRetry = TimeSpan.FromSeconds(2);

    /// <summary>On quit, how long to wait for queued mouse work (letting go of held keys and buttons, restoring the pointer) to finish.</summary>
    public static readonly TimeSpan MouseQueueDrain = TimeSpan.FromSeconds(1);

    /// <summary>The web search action sends Ctrl+C, then waits this long for the program to put the selection on the clipboard.</summary>
    public static readonly TimeSpan MouseCopyWait = TimeSpan.FromMilliseconds(150);

    /// <summary>A display level OmniControl set or read this recently is trusted for the next step instead of asking the display again (DDC/CI reads take 40 ms or more).</summary>
    public static readonly TimeSpan DisplayLevelTrust = TimeSpan.FromSeconds(10);

    /// <summary>While the Gestures page shows the gesture areas on screen, how often they are looked up again (the active window's title bar moves).</summary>
    public static readonly TimeSpan GestureMarginsRefresh = TimeSpan.FromMilliseconds(250);

    /// <summary>The displays are looked at again (to see whether one was plugged in or removed) at most this often, however many events ask for it.</summary>
    public static readonly TimeSpan DisplayRescanGap = TimeSpan.FromSeconds(4);

    /// <summary>After Windows reports a display change, wait this long for it to settle before looking at the displays.</summary>
    public static readonly TimeSpan DisplayChangeSettle = TimeSpan.FromMilliseconds(1500);

    /// <summary>What the PC offers for the sound modes (Windows version, virtual cables, Equalizer APO) is looked at again at most this often.</summary>
    public static readonly TimeSpan SoundModeRecheckGap = TimeSpan.FromSeconds(3);

    /// <summary>How often the effects engine looks at its running programs (sound arriving, silence checks, delay shown).</summary>
    public static readonly TimeSpan EngineTick = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// A chain edit (an EQ drag sends one per pixel) reaches the running engine at once, then at most once per this gap; the last
    /// edit is always applied. Also limits how often the master's chain file is rewritten for the APO.
    /// </summary>
    public static readonly TimeSpan EngineChainApplyGap = TimeSpan.FromMilliseconds(40);

    /// <summary>A changed effect value (EQ band, gain, wet mix) glides to its new value over this time, so a big change never clicks.</summary>
    public static readonly TimeSpan EffectGlide = TimeSpan.FromMilliseconds(30);

    /// <summary>After the safety limiter turned a peak down, the level comes back with this time constant.</summary>
    public static readonly TimeSpan LimiterRelease = TimeSpan.FromMilliseconds(150);

    /// <summary>After muting the original, the first part of the check is skipped (the mute takes a moment to reach the capture).</summary>
    public static readonly TimeSpan CaptureSilenceSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// After muting the original, the capture must deliver sound within this time, or muting is taken to have silenced the capture
    /// too: the original is unmuted at once and plays without effects.
    /// </summary>
    public static readonly TimeSpan CaptureSilenceProbe = TimeSpan.FromMilliseconds(1500);

    /// <summary>"Test effects on this program": how long it listens before muting, and how long it plays the processed sound.</summary>
    public static readonly TimeSpan CaptureTestListen = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan CaptureTestPlay = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// Equalizer APO opens a config file for a moment to read it after every change; a write that meets it waits this long and
    /// tries again, at most <see cref="ApoFileAttempts"/> times in all.
    /// </summary>
    public static readonly TimeSpan ApoFileRetry = TimeSpan.FromMilliseconds(10);
    public const int ApoFileAttempts = 5;

    /// <summary>
    /// At exit, how long unloading the plug-ins (VST3 and VST2) may take before OmniControl ends its own process (a plug-in's teardown
    /// can hang; by then the sound is back and every setting is saved).
    /// </summary>
    public static readonly TimeSpan PluginUnloadLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// While a plug-in's own editor is shown, how often it gets the host's idle call (VST2 editors draw and take their new size from it;
    /// about 30 a second, TabForge's rate).
    /// </summary>
    public static readonly TimeSpan PluginEditorIdle = TimeSpan.FromMilliseconds(33);

    /// <summary>How often the FX window reads its row's live numbers (latency, CPU); each CPU reading is the share over this period.</summary>
    public static readonly TimeSpan FxAnalysisRefresh = TimeSpan.FromMilliseconds(500);

    /// <summary>The pointer rests on the FX window's response thumbnail this long before the larger graph opens.</summary>
    public static readonly TimeSpan FxGraphHoverOpen = TimeSpan.FromMilliseconds(300);

    /// <summary>The larger graph stays open this long after the pointer left the thumbnail and the graph, so the gap between them can be crossed.</summary>
    public static readonly TimeSpan FxGraphHoverClose = TimeSpan.FromMilliseconds(150);

    /// <summary>Slide animation when a dragged list item makes room.</summary>
    public static readonly TimeSpan DragSlide = TimeSpan.FromMilliseconds(110);

    /// <summary>How long an input's card on the Mouse and Trackpad page stays lit after the real button is pressed or the gesture recognised.</summary>
    public static readonly TimeSpan MouseFlash = TimeSpan.FromMilliseconds(180);

    /// <summary>Least time between two recognised trackpad gestures (a bouncing finger or a quick second touch is not a new gesture).</summary>
    public static readonly TimeSpan TrackpadGestureCooldown = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// After OmniControl swaps the primary and secondary mouse buttons, the swap is put back unless "Keep" is chosen within this time,
    /// so a swap the user can't work with (or didn't mean) never stays.
    /// </summary>
    public static readonly TimeSpan PointerSwapKeep = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A mouse button may start at most <see cref="LaunchBurstMax"/> programs or folders within this time; more is treated as a runaway
    /// (a repeating or looping mapping) and refused, so it can never flood the desktop with windows.
    /// </summary>
    public static readonly TimeSpan LaunchBurstWindow = TimeSpan.FromSeconds(5);
    public const int LaunchBurstMax = 3;

    /// <summary>A custom gesture rule runs at most once per this time unless its own rate limit says otherwise (the default of a new rule).</summary>
    public static readonly TimeSpan RuleRateLimit = TimeSpan.FromSeconds(1);

    /// <summary>The rate limits a rule's editor offers (its drop-down), shortest first; <see cref="RuleRateLimit"/> is one of them.</summary>
    public static readonly IReadOnlyList<TimeSpan> RuleRateLimits =
        new[] { 100, 250, 500, 1000, 2000, 5000, 10000 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray();

    /// <summary>"Press and hold" triggers: how long the button must be held, unless the trigger's own setting says otherwise.</summary>
    public static readonly TimeSpan RuleHold = TimeSpan.FromMilliseconds(600);

    /// <summary>A rule's command is stopped (with everything it started) after this long, unless the step's own timeout says otherwise.</summary>
    public static readonly TimeSpan RuleCommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A rule's "Wait" step waits this long unless set otherwise.</summary>
    public static readonly TimeSpan RuleWait = TimeSpan.FromMilliseconds(500);
}
