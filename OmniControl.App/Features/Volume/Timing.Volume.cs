namespace OmniControl.App.Shared.Services;

// The intervals and limits of the mixer, the effects engine, Equalizer APO and plug-ins; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How often the mixer re-reads the programs from Windows while a view that shows them is on screen (the Volume page, an FX window, the
    /// tray panel), and while none is (<see cref="MixerRefreshIdle"/>). Both are backstops: a program opening a new session is seen at once
    /// through Windows' session event, one starting or stopping its sound through <see cref="ProgramSoundPoll"/>, and the devices through
    /// Windows' device notices.
    public static readonly TimeSpan MixerRefresh = TimeSpan.FromSeconds(2), MixerRefreshIdle = TimeSpan.FromSeconds(60);

    /// How often the mixer asks Windows whether the sessions of programs with effects started or stopped playing (one cheap call per
    /// session); a change starts a full scan at once, so a run that waits for sound (a cable) starts within this time.
    public static readonly TimeSpan ProgramSoundPoll = TimeSpan.FromMilliseconds(150);

    /// After the user changes a program's volume or device, ignore what Windows reports for this long.
    public static readonly TimeSpan MixerUserChangeGrace = TimeSpan.FromSeconds(3);

    /// A volume notification that matches a level the cable mirror wrote this recently is its own echo and is not copied back.
    public static readonly TimeSpan MirrorEchoGrace = TimeSpan.FromSeconds(2);

    /// After the user moves a master or input slider, don't overwrite it with the system value for this long.
    public static readonly TimeSpan SliderEchoGrace = TimeSpan.FromSeconds(2);

    /// Refresh rate of the Volume page's level meters (25 per second).
    public static readonly TimeSpan MeterFrame = TimeSpan.FromMilliseconds(40);

    /// The level meters (Volume page, compressor and limiter cards) read this often while all of them are at silence (a start of sound shows within it).
    public static readonly TimeSpan MeterQuietFrame = TimeSpan.FromMilliseconds(120);

    /// Below this master volume (percent) a display feed is not level-matched (1 / volume would be a gain over 50) and says it is not fed.
    public const int DisplayFeedMinPercent = 2;

    /// How often the Parametric equaliser graph's live spectrum is worked out and drawn again (about 15 per second).
    public static readonly TimeSpan SpectrumFrame = TimeSpan.FromMilliseconds(66);

    /// What the PC offers the effects engine (Windows version, virtual cables, Equalizer APO) is looked at again at most this often.
    public static readonly TimeSpan SupportRecheckGap = TimeSpan.FromSeconds(3);

    /// How often the effects engine looks at its running programs (sound arriving, silence checks, delay shown).
    public static readonly TimeSpan EngineTick = TimeSpan.FromMilliseconds(100);

    /// The master bus hears nothing while sound is playing into its cable for this long: it is given up and Windows' default goes back to the real device.
    public static readonly TimeSpan BusDeafLimit = TimeSpan.FromSeconds(3);

    /// A program held on its own cable is still heard on another device for this long: Windows has not moved its stream, so its row
    /// stops saying the effects run and asks for the program to be paused and played (moving a stream takes a moment).
    public static readonly TimeSpan CableMoveLimit = TimeSpan.FromSeconds(3);

    /// A chain edit (an EQ drag sends one per pixel) reaches the running engine at once, then at most once per this gap; the last
    /// edit is always applied. Also limits how often the master's chain file is rewritten for the APO.
    public static readonly TimeSpan EngineChainApplyGap = TimeSpan.FromMilliseconds(40);

    /// A changed effect value (EQ band, gain, wet mix) glides to its new value over this time, so a big change never clicks.
    public static readonly TimeSpan EffectGlide = TimeSpan.FromMilliseconds(30);

    /// After the safety limiter turned a peak down, the level comes back with this time constant.
    public static readonly TimeSpan LimiterRelease = TimeSpan.FromMilliseconds(150);

    /// "Test effects on this program": after silencing the original, the first part of the check is skipped (the change takes a moment to reach the recording).
    public static readonly TimeSpan CaptureSilenceSettle = TimeSpan.FromMilliseconds(300);

    /// A recorded program whose original is held silent (and the Test after silencing): once Windows' meter shows it playing, the recording must deliver sound that
    /// passes (<see cref="Features.Volume.Audio.QuietLevel.KeepsSound"/>) within this time, or silencing is taken to have silenced the recording too
    /// (or not to reach it): the original is put back at once and plays without effects.
    public static readonly TimeSpan CaptureSilenceProbe = TimeSpan.FromMilliseconds(1500);

    /// A recorded program's row says "Effects running" while sound passes and "Effects ready" after this long without any.
    public static readonly TimeSpan EngineIdleAfter = TimeSpan.FromSeconds(2);

    /// The delay a row shows is rounded to the nearest <see cref="DelayStepMs"/> and changes only after the same new figure was read
    /// <see cref="DelayStableReadings"/> engine ticks in a row, so it never flickers.
    public const int DelayStepMs = 5;

    public const int DelayStableReadings = 5;

    /// "Test effects on this program": how long it listens before muting, and how long it plays the processed sound.
    public static readonly TimeSpan CaptureTestListen = TimeSpan.FromMilliseconds(700);

    public static readonly TimeSpan CaptureTestPlay = TimeSpan.FromMilliseconds(2500);

    /// Equalizer APO opens a config file for a moment to read it after every change; a write that meets it waits this long and
    /// tries again, at most <see cref="ApoFileAttempts"/> times in all.
    public static readonly TimeSpan ApoFileRetry = TimeSpan.FromMilliseconds(10);

    public const int ApoFileAttempts = 5;

    /// Keeping OmniControl's effects in Equalizer APO's config (<see cref="Features.Volume.Audio.Engine.ApoKeeper"/>): after a change in the config folder the
    /// files are read back once the other writer had <see cref="ApoChangeSettle"/> to finish, at most once per <see cref="ApoCheckGap"/>; the
    /// slow watchdog reads them every <see cref="ApoWatchdog"/> anyway (a missed change notice, a folder that could not be watched).
    public static readonly TimeSpan ApoChangeSettle = TimeSpan.FromMilliseconds(150);

    public static readonly TimeSpan ApoCheckGap = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan ApoWatchdog = TimeSpan.FromSeconds(10), ApoWatchdogWatched = TimeSpan.FromSeconds(60);

    /// Two watchdog looks more than this many periods apart mean the PC slept in between: what the PC offers and the config files are looked at again.
    public const int ApoResumeLooks = 3;

    /// OmniControl's include line or file is put back at most once per <see cref="ApoRestoreGap"/> (a later one waits for the gap, so it is
    /// never lost); <see cref="ApoRestoreLoopCount"/> put-backs inside <see cref="ApoRestoreLoopWindow"/> mean another program keeps taking
    /// them out, which is said once.
    public static readonly TimeSpan ApoRestoreGap = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan ApoRestoreLoopWindow = TimeSpan.FromMinutes(1);

    public const int ApoRestoreLoopCount = 3;

    /// A config write that failed (a clean-up or a chain) is tried again no sooner than this, so a locked folder is not rewritten on every change.
    public static readonly TimeSpan ApoRetryGap = TimeSpan.FromSeconds(5);

    /// After the last write of a burst (an EQ drag, a put-back) OmniControl's file is touched once more after this pause, so Equalizer APO,
    /// which may have been reading while the burst went on, loads the final chain.
    public static readonly TimeSpan ApoReloadSettle = TimeSpan.FromMilliseconds(300);

    /// At exit, how long unloading the plug-ins (VST3 and VST2) may take before OmniControl ends its own process (a plug-in's teardown
    /// can hang; by then the sound is back and every setting is saved).
    public static readonly TimeSpan PluginUnloadLimit = TimeSpan.FromSeconds(3);

    /// While a plug-in's own editor is shown, how often it gets the host's idle call (VST2 editors draw and take their new size from it;
    /// about 30 a second, TabForge's rate).
    public static readonly TimeSpan PluginEditorIdle = TimeSpan.FromMilliseconds(33);

    /// How often the FX window reads its row's live Latency (a plug-in may change its latency while it runs).
    public static readonly TimeSpan FxAnalysisRefresh = TimeSpan.FromMilliseconds(500);

    /// The pointer rests on the FX window's response thumbnail this long before the larger graph opens.
    public static readonly TimeSpan FxGraphHoverOpen = TimeSpan.FromMilliseconds(300);

    /// The larger graph stays open this long after the pointer left the thumbnail and the graph, so the gap between them can be crossed.
    public static readonly TimeSpan FxGraphHoverClose = TimeSpan.FromMilliseconds(150);

    /// How long OmniControl waits for the user to press their microphone mute key when learning it.
    public static readonly TimeSpan MicKeyLearn = TimeSpan.FromSeconds(8);

    /// Keys pressed this soon after learning starts are ignored (the repeat of the key that started it).
    public static readonly TimeSpan MicKeyLearnSettle = TimeSpan.FromMilliseconds(500);

    /// After sending the learned mute key, the microphone's mute is read again this much later; if it is not as wanted, the normal mute is used.
    public static readonly TimeSpan MicKeyCheckDelay = TimeSpan.FromMilliseconds(800);

    /// The learned mute key is sent at most <see cref="MicKeyBurstMax"/> times per this window (a runaway rule can never hammer the keyboard).
    public static readonly TimeSpan MicKeyBurstWindow = TimeSpan.FromSeconds(2);

    public const int MicKeyBurstMax = 4;

    /// The list of installed programs behind the Mixer's "Add program" menu is read again at most this often (reading the Start menu is slow).
    public static readonly TimeSpan InstalledProgramsCache = TimeSpan.FromMinutes(5);

    /// How long the Easy page's confirmation line ("Your sound is on: Bass boost") stays before it fades away.
    public static readonly TimeSpan EasyConfirmation = TimeSpan.FromSeconds(6);
}
