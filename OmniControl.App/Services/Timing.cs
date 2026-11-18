namespace OmniControl.App.Services;

/// <summary>Every interval and grace period the app uses, in one place, so a timing change never means a hunt through the code.</summary>
public static class Timing
{
    /// <summary>
    /// How often the mixer re-reads programs and devices from Windows (the backstop: a program opening a new session is seen at once
    /// through Windows' session event, and one starting or stopping its sound through <see cref="ProgramSoundPoll"/>).
    /// </summary>
    public static readonly TimeSpan MixerRefresh = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the mixer asks Windows whether the sessions of programs with effects started or stopped playing (one cheap call per
    /// session); a change starts a full scan at once, so a run that waits for sound (a cable) starts within this time.
    /// </summary>
    public static readonly TimeSpan ProgramSoundPoll = TimeSpan.FromMilliseconds(150);

    /// <summary>After the user changes a program's volume or device, ignore what Windows reports for this long.</summary>
    public static readonly TimeSpan MixerUserChangeGrace = TimeSpan.FromSeconds(3);

    /// <summary>A volume notification that matches a level the cable mirror wrote this recently is its own echo and is not copied back.</summary>
    public static readonly TimeSpan MirrorEchoGrace = TimeSpan.FromSeconds(2);

    /// <summary>After the user moves a master or input slider, don't overwrite it with the system value for this long.</summary>
    public static readonly TimeSpan SliderEchoGrace = TimeSpan.FromSeconds(2);

    /// <summary>How long a pause ends the letters typed to jump to an entry of an open drop-down list.</summary>
    public static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    /// <summary>Refresh rate of the Volume page's level meters (25 per second).</summary>
    public static readonly TimeSpan MeterFrame = TimeSpan.FromMilliseconds(40);

    /// <summary>Below this master volume (percent) a display feed is not level-matched (1 / volume would be a gain over 50) and says it is not fed.</summary>
    public const int DisplayFeedMinPercent = 2;

    /// <summary>How often the Parametric equaliser graph's live spectrum is worked out and drawn again (about 15 per second).</summary>
    public static readonly TimeSpan SpectrumFrame = TimeSpan.FromMilliseconds(66);

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

    /// <summary>
    /// OSD placement, in device-independent pixels (a place's offsets are stored in them, so a place survives a change of resolution or
    /// scale): the gap the seven built-in places keep from the screen edge; how close a dragged OSD must come to an edge, a margin line or
    /// the centre lines to snap to it; how far an arrow key (or Shift + arrow) nudges it; and the most an offset may be.
    /// </summary>
    public const int OsdMargin = 32, OsdSnap = 8, OsdNudge = 1, OsdNudgeLarge = 10, OsdOffsetMax = 1200;

    /// <summary>Whole pixels added to a measured pop-up's size so rounding never puts it a pixel over the margin.</summary>
    public const int OsdRoundingSlack = 2;

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

    /// <summary>
    /// The pause between one action and the next when a button or gesture has several, so a program has taken the first keystroke
    /// or window change before the next one arrives.
    /// </summary>
    public static readonly TimeSpan MouseActionGap = TimeSpan.FromMilliseconds(40);

    /// <summary>Repeated clicking: the hard ceiling on its speed, whatever is set (clicks per second).</summary>
    public const int ClickSpeedMax = 50;

    /// <summary>Repeated clicking: how often the loop looks at Esc, the pointer and the stop limits while it waits between clicks.</summary>
    public static readonly TimeSpan ClickWatchEvery = TimeSpan.FromMilliseconds(10);

    /// <summary>Repeated clicking: how far the pointer may drift (pixels) before "stop when the mouse moves" stops it.</summary>
    public const int ClickMoveTolerance = 4;

    /// <summary>How long a mouse button waits for the one action gate's answer from the UI thread before the action is refused (the hook must never wait long).</summary>
    public static readonly TimeSpan GateAskWait = TimeSpan.FromMilliseconds(150);

    /// <summary>Repeated clicking: how often the on-screen badge is refreshed with the click count.</summary>
    public static readonly TimeSpan ClickBadgeEvery = TimeSpan.FromSeconds(1);

    /// <summary>A display level OmniControl set or read this recently is trusted for the next step instead of asking the display again (DDC/CI reads take 40 ms or more).</summary>
    public static readonly TimeSpan DisplayLevelTrust = TimeSpan.FromSeconds(10);

    /// <summary>While the Gestures page shows the gesture areas on screen, how often they are looked up again (the active window's title bar moves).</summary>
    public static readonly TimeSpan GestureMarginsRefresh = TimeSpan.FromMilliseconds(250);

    /// <summary>The displays are looked at again (to see whether one was plugged in or removed) at most this often, however many events ask for it.</summary>
    public static readonly TimeSpan DisplayRescanGap = TimeSpan.FromSeconds(4);

    /// <summary>After Windows announces a hardware change (<see cref="HardwareWatches"/>), wait this long for the burst to end (a Remote Desktop return makes several) before refreshing what depends on it.</summary>
    public static readonly TimeSpan HardwareDebounce = TimeSpan.FromSeconds(2);

    /// <summary>
    /// After an announcement, a watch that can tell when it looks stale is refreshed at these times after the last one of the burst, stopping as
    /// soon as it looks right (<see cref="HardwareRetry"/>): after a return from Remote Desktop the monitors often do not answer DDC/CI for 2-10 s.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> HardwareRetrySchedule =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)];

    /// <summary>One watched piece of hardware is refreshed at most this often, however many announcements arrive; a later one still gets its refresh once the gap is over.</summary>
    public static readonly TimeSpan HardwareRefreshGap = TimeSpan.FromSeconds(4);

    /// <summary>An action that finds its picked device missing makes its watch refresh once; the same watch is not refreshed again for an action within this time (a held key on an unplugged monitor).</summary>
    public static readonly TimeSpan HardwareOnDemandGap = TimeSpan.FromSeconds(10);

    /// <summary>How often the warm-light schedule looks at the clock; its ease in and out is 30 minutes, so every look moves the picture by a small step.</summary>
    public static readonly TimeSpan WarmLightTick = TimeSpan.FromSeconds(30);

    /// <summary>What the PC offers the effects engine (Windows version, virtual cables, Equalizer APO) is looked at again at most this often.</summary>
    public static readonly TimeSpan SupportRecheckGap = TimeSpan.FromSeconds(3);

    /// <summary>How often the effects engine looks at its running programs (sound arriving, silence checks, delay shown).</summary>
    public static readonly TimeSpan EngineTick = TimeSpan.FromMilliseconds(100);

    /// <summary>The master bus hears nothing while sound is playing into its cable for this long: it is given up and Windows' default goes back to the real device.</summary>
    public static readonly TimeSpan BusDeafLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A program held on its own cable is still heard on another device for this long: Windows has not moved its stream, so its row
    /// stops saying the effects run and asks for the program to be paused and played (moving a stream takes a moment).
    /// </summary>
    public static readonly TimeSpan CableMoveLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A chain edit (an EQ drag sends one per pixel) reaches the running engine at once, then at most once per this gap; the last
    /// edit is always applied. Also limits how often the master's chain file is rewritten for the APO.
    /// </summary>
    public static readonly TimeSpan EngineChainApplyGap = TimeSpan.FromMilliseconds(40);

    /// <summary>A changed effect value (EQ band, gain, wet mix) glides to its new value over this time, so a big change never clicks.</summary>
    public static readonly TimeSpan EffectGlide = TimeSpan.FromMilliseconds(30);

    /// <summary>After the safety limiter turned a peak down, the level comes back with this time constant.</summary>
    public static readonly TimeSpan LimiterRelease = TimeSpan.FromMilliseconds(150);

    /// <summary>"Test effects on this program": after silencing the original, the first part of the check is skipped (the change takes a moment to reach the recording).</summary>
    public static readonly TimeSpan CaptureSilenceSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// A recorded program whose original is held silent (and the Test after silencing): once Windows' meter shows it playing, the recording must deliver sound that
    /// passes (<see cref="Audio.QuietLevel.KeepsSound"/>) within this time, or silencing is taken to have silenced the recording too
    /// (or not to reach it): the original is put back at once and plays without effects.
    /// </summary>
    public static readonly TimeSpan CaptureSilenceProbe = TimeSpan.FromMilliseconds(1500);

    /// <summary>A recorded program's row says "Effects running" while sound passes and "Effects ready" after this long without any.</summary>
    public static readonly TimeSpan EngineIdleAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The delay a row shows is rounded to the nearest <see cref="DelayStepMs"/> and changes only after the same new figure was read
    /// <see cref="DelayStableReadings"/> engine ticks in a row, so it never flickers.
    /// </summary>
    public const int DelayStepMs = 5;
    public const int DelayStableReadings = 5;

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
    /// Keeping OmniControl's effects in Equalizer APO's config (<see cref="Audio.Engine.ApoKeeper"/>): after a change in the config folder the
    /// files are read back once the other writer had <see cref="ApoChangeSettle"/> to finish, at most once per <see cref="ApoCheckGap"/>; the
    /// slow watchdog reads them every <see cref="ApoWatchdog"/> anyway (a missed change notice, a folder that could not be watched).
    /// </summary>
    public static readonly TimeSpan ApoChangeSettle = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ApoCheckGap = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ApoWatchdog = TimeSpan.FromSeconds(10);

    /// <summary>Two watchdog looks this far apart mean the PC slept in between: what the PC offers and the config files are looked at again.</summary>
    public static readonly TimeSpan ApoResumeGap = TimeSpan.FromSeconds(30);

    /// <summary>
    /// OmniControl's include line or file is put back at most once per <see cref="ApoRestoreGap"/> (a later one waits for the gap, so it is
    /// never lost); <see cref="ApoRestoreLoopCount"/> put-backs inside <see cref="ApoRestoreLoopWindow"/> mean another program keeps taking
    /// them out, which is said once.
    /// </summary>
    public static readonly TimeSpan ApoRestoreGap = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ApoRestoreLoopWindow = TimeSpan.FromMinutes(1);
    public const int ApoRestoreLoopCount = 3;

    /// <summary>A config write that failed (a clean-up or a chain) is tried again no sooner than this, so a locked folder is not rewritten on every change.</summary>
    public static readonly TimeSpan ApoRetryGap = TimeSpan.FromSeconds(5);

    /// <summary>
    /// After the last write of a burst (an EQ drag, a put-back) OmniControl's file is touched once more after this pause, so Equalizer APO,
    /// which may have been reading while the burst went on, loads the final chain.
    /// </summary>
    public static readonly TimeSpan ApoReloadSettle = TimeSpan.FromMilliseconds(300);

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

    /// <summary>How often the automation countdown window counts down (it shows whole seconds).</summary>
    public static readonly TimeSpan CountdownTick = TimeSpan.FromSeconds(1);

    /// <summary>How often the FX window reads its row's live Latency (a plug-in may change its latency while it runs).</summary>
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
    /// The backstop look at Windows' touchpad settings (a few reads, no writes unless something turned a gesture OmniControl keeps off back
    /// on); a write to the settings key is seen at once without waiting for it.
    /// </summary>
    public static readonly TimeSpan TrackpadTakeoverCheck = TimeSpan.FromSeconds(3);

    /// <summary>
    /// While the takeover's safety net watches for a panel Windows opened after a gesture (for <c>CoreTiming.TrackpadSurfaceWatchMs</c>),
    /// it looks at the window in front this often: short enough that a panel is closed before it has finished opening.
    /// </summary>
    public static readonly TimeSpan TrackpadSurfaceLook = TimeSpan.FromMilliseconds(20);

    /// <summary>At most this many recognised-gesture lines go to the trackpad log per window (takeover lines are never limited).</summary>
    public static readonly TimeSpan TrackpadLogGestureWindow = TimeSpan.FromMinutes(1);
    public const int TrackpadLogGestureLimit = 30;

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

    /// <summary>How long OmniControl waits for the user to press their microphone mute key when learning it.</summary>
    public static readonly TimeSpan MicKeyLearn = TimeSpan.FromSeconds(8);

    /// <summary>Keys pressed this soon after learning starts are ignored (the repeat of the key that started it).</summary>
    public static readonly TimeSpan MicKeyLearnSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>After sending the learned mute key, the microphone's mute is read again this much later; if it is not as wanted, the normal mute is used.</summary>
    public static readonly TimeSpan MicKeyCheckDelay = TimeSpan.FromMilliseconds(800);

    /// <summary>The learned mute key is sent at most <see cref="MicKeyBurstMax"/> times per this window (a runaway rule can never hammer the keyboard).</summary>
    public static readonly TimeSpan MicKeyBurstWindow = TimeSpan.FromSeconds(2);
    public const int MicKeyBurstMax = 4;

    /// <summary>The installed apps (the "Open ..." actions) are looked for again when the window is shown or a picker listing them opens, but not more often than this.</summary>
    public static readonly TimeSpan AppScanGap = TimeSpan.FromSeconds(30);

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

    /// <summary>The list of installed programs behind the Mixer's "Add program" menu is read again at most this often (reading the Start menu is slow).</summary>
    public static readonly TimeSpan InstalledProgramsCache = TimeSpan.FromMinutes(5);

    /// <summary>How long the preset manager offers "Undo" after presets were deleted.</summary>
    public static readonly TimeSpan PresetUndo = TimeSpan.FromSeconds(8);

    /// <summary>How long the Easy page's confirmation line ("Your sound is on: Bass boost") stays before it fades away.</summary>
    public static readonly TimeSpan EasyConfirmation = TimeSpan.FromSeconds(6);

    /// <summary>How long "Undo" stays beside a "Restore defaults" confirmation (the Easy page's and every other page's).</summary>
    public static readonly TimeSpan RestoreUndo = TimeSpan.FromSeconds(60);

    /// <summary>How long a "Restore defaults" line without an Undo ("Already at the defaults.") stays.</summary>
    public static readonly TimeSpan RestoreNote = TimeSpan.FromSeconds(6);

    /// <summary>How often the open page's "Restore defaults" button looks again at whether the page is at its defaults.</summary>
    public static readonly TimeSpan RestoreStateCheck = TimeSpan.FromSeconds(1);

    // ---- Automation (Pages/Automation, App/Automation) ----

    /// <summary>How often the automations look at the clock, the battery, the idle time, the running programs and the devices.</summary>
    public static readonly TimeSpan AutomationTick = TimeSpan.FromSeconds(5);

    // ---- Power journal and wakes (Services/PowerJournal, Core/Engine/WakeDetector and ClockGap) ----

    /// <summary>How often the clock check samples (and a waiting wake candidate is looked at again).</summary>
    public static readonly TimeSpan PowerClockTick = TimeSpan.FromSeconds(2);

    /// <summary>A clock sample this much later than asked means the program was stopped (the PC slept, or Modern Standby froze it): one piece of wake evidence.</summary>
    public static readonly TimeSpan PowerClockGap = TimeSpan.FromSeconds(20);

    /// <summary>A wake told by one channel waits this long for a second one (<see cref="Core.Engine.WakeDetector"/>).</summary>
    public static readonly TimeSpan WakeConfirmWindow = TimeSpan.FromSeconds(20);

    /// <summary>After a confirmed wake, more wake evidence within this time is the same wake told again, unless the PC went down in between.</summary>
    public static readonly TimeSpan WakeQuiet = TimeSpan.FromMinutes(2);

    /// <summary>The display must have been off at least this long for its return to count as wake evidence (Modern Standby; a short blank is not).</summary>
    public static readonly TimeSpan StandbyDisplayOffMin = TimeSpan.FromSeconds(30);

    /// <summary>The same kind of power event told by several channels within this time is one journal entry.</summary>
    public static readonly TimeSpan PowerEventSame = TimeSpan.FromSeconds(5);

    /// <summary>"At log in" runs once per sign-in: a start-up whose Windows start time is within this of the last one that ran it, in the same session, is the same sign-in.</summary>
    public static readonly TimeSpan LoginSameBoot = TimeSpan.FromMinutes(2);

    // ---- Steps that need the network or a device (Rules/ReadyWait, Hotkeys/ActionReach) ----

    /// <summary>After a wake or a log-in, the waits between readiness checks (network up, device answers); the last repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> ReadyBackoff =
        new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(20) };

    /// <summary>The longest a step waits for the network and its device before it is reported as not reachable.</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The waits before a failed step is tried again; the last repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> StepRetryBackoff = new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) };

    /// <summary>How many times in all a step that needs a device is tried.</summary>
    public const int StepTriesMax = 3;

    /// <summary>How often a step looks whether its device's command has ended, and how long nothing may be running before it counts as "nothing ran".</summary>
    public static readonly TimeSpan StepOutcomePoll = TimeSpan.FromMilliseconds(500), StepOutcomeIdle = TimeSpan.FromSeconds(10);

    /// <summary>The longest a step waits for its device's command to end (a simulation can take two minutes).</summary>
    public static readonly TimeSpan StepOutcomeWait = TimeSpan.FromMinutes(3);

    /// <summary>A time-of-day automation whose time passed while the PC slept still runs if it woke within this time of it.</summary>
    public static readonly TimeSpan AutomationCatchUp = TimeSpan.FromMinutes(10);

    /// <summary>An automation never starts again within this time of its last start.</summary>
    public static readonly TimeSpan AutomationMinGap = TimeSpan.FromSeconds(10);

    /// <summary>An automation that starts more than <see cref="AutomationBurstMax"/> times within this window is paused (it is looping).</summary>
    public static readonly TimeSpan AutomationBurstWindow = TimeSpan.FromMinutes(1);
    public const int AutomationBurstMax = 5;

    /// <summary>
    /// The most rules one chain may hold: a rule that runs an action (or changes a state) that starts another rule, and so on. The rule
    /// that would make the chain longer is not started, and a rule never starts from its own chain (no loops).
    /// </summary>
    public const int AutomationChainMax = 3;

    // ---- Other PCs (OtherPcs/): the LAN link between paired PCs ----

    /// <summary>How often this PC announces itself on the local network while Other PCs is in use.</summary>
    public static readonly TimeSpan PcBeaconEvery = TimeSpan.FromSeconds(4);

    /// <summary>How long "Pair a PC" keeps this PC open to a pairing (then it closes by itself).</summary>
    public static readonly TimeSpan PcPairingOpen = TimeSpan.FromMinutes(2);

    /// <summary>Each step of a connection's start (a pairing's messages, a session's proof) must arrive within this time, or the connection is dropped.</summary>
    public static readonly TimeSpan PcHandshakeStep = TimeSpan.FromSeconds(8);

    /// <summary>How often a paired PC that is seen but not connected is dialled, and how often a live link is checked and sends its keep-alive.</summary>
    public static readonly TimeSpan PcLinkCheck = TimeSpan.FromSeconds(5);

    /// <summary>The route probe (an authenticated ping answered with a pong): how often it runs for each paired PC, how long it waits, and how long a success counts as connected.</summary>
    public static readonly TimeSpan PcProbeEvery = TimeSpan.FromSeconds(15), PcProbeTimeout = TimeSpan.FromSeconds(4), PcProbeFresh = TimeSpan.FromSeconds(40);

    /// <summary>A link that has sent nothing (not even its keep-alive) for this long is closed.</summary>
    public static readonly TimeSpan PcLinkSilence = TimeSpan.FromSeconds(30);

    /// <summary>A change in what is shared waits this long for more changes before it is sent (a page's actions register in a burst).</summary>
    public static readonly TimeSpan PcShareSettle = TimeSpan.FromMilliseconds(400);

    /// <summary>A paired PC may start at most <see cref="PcRunBurstMax"/> actions here within this time; more is treated as a runaway and dropped.</summary>
    public static readonly TimeSpan PcRunBurstWindow = TimeSpan.FromSeconds(5);
    public const int PcRunBurstMax = 8;

    /// <summary>A level dragged here for another PC is sent at most this often (only the newest value); the other PC accepts at most <see cref="PcSetBurstMax"/> sets per <see cref="PcSetBurstWindow"/>.</summary>
    public static readonly TimeSpan PcSetEvery = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan PcSetBurstWindow = TimeSpan.FromSeconds(2);
    public const int PcSetBurstMax = 40;

    /// <summary>How long a request for another PC's device set-ups waits for the answer, and how many such requests one paired PC may make of this one within the window.</summary>
    public static readonly TimeSpan PcDevicesWait = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PcDevicesBurstWindow = TimeSpan.FromSeconds(10);
    public const int PcDevicesBurstMax = 4;

    /// <summary>A card of another PC's state ignores that PC's reports for this long after the user moved it here (the echo of the user's own change).</summary>
    public static readonly TimeSpan PcEchoGrace = TimeSpan.FromSeconds(1);

    /// <summary>At most this many connections may be in their start (before they proved who they are) at once.</summary>
    public const int PcHandshakesMax = 4;

    /// <summary>How often a Tailscale-paired PC that is offline gets the Tailscale checks again, how long to wait after a fix (start, sign in) before reading Tailscale again, and how long to wait before trying to open a Tailscale link again after one failed.</summary>
    public static readonly TimeSpan PcTailscaleRecheck = TimeSpan.FromSeconds(30), PcTailscaleAfterFix = TimeSpan.FromSeconds(3), PcTailscaleSwitchBackoff = TimeSpan.FromMinutes(2);

    /// <summary>The longest "then wait" between two actions of an automation, in seconds, and what a new one starts at.</summary>
    public const int AutomationMaxWaitSeconds = 600, AutomationDefaultWaitSeconds = 5;

    /// <summary>Sleep, hibernate, shut down, restart and sign out never happen within this time of OmniControl starting.</summary>
    public static readonly TimeSpan PowerActionGrace = TimeSpan.FromMinutes(2);

    /// <summary>"Turn off displays" waits this long first, so the click or key that started it does not wake the screens again at once.</summary>
    public static readonly TimeSpan DisplaysOffGrace = TimeSpan.FromSeconds(1);

    /// <summary>The countdown (with Cancel) before sleep, hibernate, shut down, restart or sign out.</summary>
    public static readonly TimeSpan PowerCountdown = TimeSpan.FromSeconds(20);

    /// <summary>The shortest and longest countdown an input may ask for (never none: the user must always be able to cancel).</summary>
    public const int PowerCountdownMinSeconds = 5, PowerCountdownMaxSeconds = 300;

    /// <summary>A custom action's command or script is stopped (with everything it started) after this long.</summary>
    public static readonly TimeSpan CustomActionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>A smart plug command (kasa) is stopped after this long.</summary>
    public static readonly TimeSpan DeviceCommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The quick looks at what is installed for the Devices page (is python-kasa there) are stopped after this long.</summary>
    public static readonly TimeSpan DeviceProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Installing python-kasa with pip (a download) is stopped after this long.</summary>
    public static readonly TimeSpan DeviceInstallTimeout = TimeSpan.FromMinutes(5);

    /// <summary>A simulated device's step waits this long for its element or text to show, looking again every poll; after a tap, typing or swipe it lets the screen settle; a whole run is stopped after the run timeout.</summary>
    public static readonly TimeSpan DeviceSimElementWait = TimeSpan.FromSeconds(10), DeviceSimPoll = TimeSpan.FromMilliseconds(700),
        DeviceSimSettle = TimeSpan.FromMilliseconds(600), DeviceSimRunTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How often the Smart Devices page looks again for a newly installed emulator or adb while either is still missing.</summary>
    public static readonly TimeSpan DeviceSimRecheck = TimeSpan.FromSeconds(5);

    /// <summary>How often adb is asked which emulators are up, so the set-up checklist follows an emulator opening or closing; and how long a tap on a home screen waits for the app to open.</summary>
    public static readonly TimeSpan DeviceSimLive = TimeSpan.FromSeconds(3), DeviceSimLaunchWait = TimeSpan.FromSeconds(1.5);

    /// <summary>How often the memory the emulator's programs hold is read again for the Simulation row's chip (a gentle background look; the figure only needs to be roughly right).</summary>
    public static readonly TimeSpan EmulatorMemoryRefresh = TimeSpan.FromSeconds(10);

    /// <summary>"Keep running: until idle" closes the emulator after this many minutes without an action (the default; the user types another).</summary>
    public const int EmulatorIdleMinutes = 30;

    /// <summary>A woken emulator is waited for this long to finish starting (a cold boot); a quick boot from a snapshot gets the shorter time before one cold boot is tried; adb is asked every poll.</summary>
    public static readonly TimeSpan EmulatorBootTimeout = TimeSpan.FromMinutes(5), EmulatorQuickBootTimeout = TimeSpan.FromSeconds(120), EmulatorBootPoll = TimeSpan.FromSeconds(2),
        EmulatorWindowPoll = TimeSpan.FromMilliseconds(150), EmulatorWindowWait = TimeSpan.FromSeconds(60);

    /// <summary>Two reports of a tap closer than this in time and <see cref="DeviceSimSameTouchPixels"/> in place are one tap.</summary>
    public static readonly TimeSpan DeviceSimSameTouch = TimeSpan.FromMilliseconds(400);

    public const int DeviceSimSameTouchPixels = 24;

    /// <summary>How many times a step that waits for an element scrolls the page to find it, before it gives up.</summary>
    public const int DeviceSimScrollTries = 4;

    /// <summary>While steps are being recorded the screen is read again this often, so a tap is matched with the screen it was made on.</summary>
    public static readonly TimeSpan DeviceSimRecordGap = TimeSpan.FromMilliseconds(900);

    /// <summary>Teaching on and off: each "It's on now" / "It's off now" takes this many looks at the device's card, this far apart.</summary>
    public const int DeviceSimTeachLooks = 3;

    public static readonly TimeSpan DeviceSimTeachGap = TimeSpan.FromMilliseconds(500);

    /// <summary>A device's name typed in its box is saved this long after the last key (the list and its actions follow every key at once).</summary>
    public static readonly TimeSpan DeviceNameSaveDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>How often a device with "Every 5 min" / "Every 15 min" reads its real state.</summary>
    public static readonly TimeSpan DeviceAutoCheck5 = TimeSpan.FromMinutes(5), DeviceAutoCheck15 = TimeSpan.FromMinutes(15);

    /// <summary>How often the service looks at which devices' periodic check is due, and how long after start-up the first check waits (the window is up by then).</summary>
    public static readonly TimeSpan DeviceAutoCheckTick = TimeSpan.FromMinutes(1), DeviceStartCheckDelay = TimeSpan.FromSeconds(4);

    /// <summary>After a USB announcement the automations wait this long before comparing the device list.</summary>
    public static readonly TimeSpan AutomationUsbSettle = TimeSpan.FromSeconds(2);

    /// <summary>How long a restarted OmniControl waits for the run before it to end (it saves its settings as it closes) before it carries out a staged import or wipe.</summary>
    public static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(15);

    /// <summary>While the screenshot overlay is open in its strong "Stay on top" mode it puts itself back on top (and takes the foreground back) this often.</summary>
    public static readonly TimeSpan ScreenshotOnTopRefresh = TimeSpan.FromMilliseconds(250);

    /// <summary>After Stop is asked, a recording that has not finished its file within this long counts as stuck: it is said once, and the bar's X (Esc twice, the emergency key) then offers "Force close". Esc pressed twice within <see cref="RecordingEscTwice"/> asks to discard.</summary>
    public static readonly TimeSpan RecordingStopWait = TimeSpan.FromSeconds(8), RecordingEscTwice = TimeSpan.FromMilliseconds(700);

    /// <summary>The recording bar's timer is updated this often.</summary>
    public static readonly TimeSpan RecordingTimerTick = TimeSpan.FromMilliseconds(250);

    /// <summary>A screen recording stops by itself at this length (an MP4, a GIF), at this file size (an MP4, a GIF), or when the disk has less than this free.</summary>
    public static readonly TimeSpan VideoMaxLength = TimeSpan.FromHours(4), GifMaxLength = TimeSpan.FromMinutes(2);

    public const long VideoMaxBytes = 16L << 30, GifMaxBytes = 500L << 20, VideoMinFreeBytes = 1L << 30;

    /// <summary>
    /// A click on the tray icon this soon after the flyout hid itself (it hides as the press on the icon takes the focus away) only closed it:
    /// the click does not open it again, so the icon opens and closes the flyout like Windows' own tray flyouts.
    /// </summary>
    public static readonly TimeSpan TrayReopenGrace = TimeSpan.FromMilliseconds(400);

    /// <summary>Opening the panel reads its devices' states one at a time, this long apart (Tray/TrayFlyoutModel.Rechecks), so opening stays instant.</summary>
    public static readonly TimeSpan TrayRecheckStagger = TimeSpan.FromMilliseconds(400);

    /// <summary>A device's state read longer ago than this is only the last one known: its tray tile is faded with a clock.</summary>
    public static readonly TimeSpan DeviceStateStale = TimeSpan.FromMinutes(30);

    /// <summary>A tile's state is never read again sooner than this after the last read was asked for (a quick re-open of the panel asks nothing).</summary>
    public static readonly TimeSpan TrayRecheckMinGap = TimeSpan.FromSeconds(10);

    /// <summary>The icon picker shows this many cells at once after a search; the rest follow in the next frame so the first results appear immediately.</summary>
    public const int IconPickerFirstCells = 48;

    /// <summary>A key typed into a search box this soon after another waits this long before its search runs (the first letter is searched at once; <see cref="SearchRunner{T}"/>).</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(60);

    /// <summary>At most one automatic update check per this long (the daily check; "Check now" ignores it).</summary>
    public static readonly TimeSpan UpdateCheckEvery = TimeSpan.FromHours(24);

    /// <summary>The first automatic update check waits this long after start-up, so it never competes with the app settling.</summary>
    public static readonly TimeSpan UpdateCheckFirstDelay = TimeSpan.FromSeconds(45);

    /// <summary>How often the running app asks whether the daily update check is due (a PC that stays on for days).</summary>
    public static readonly TimeSpan UpdateCheckPoll = TimeSpan.FromHours(1);

    /// <summary>An update check that has not answered after this long counts as failed.</summary>
    public static readonly TimeSpan UpdateRequestTimeout = TimeSpan.FromSeconds(15);
}
