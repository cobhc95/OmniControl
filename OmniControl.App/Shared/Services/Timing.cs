namespace OmniControl.App.Shared.Services;

/// Every interval and grace period the app uses, so a timing change never means a hunt through the code: the shared ones here, each
/// feature's in its own <c>Timing.&lt;Feature&gt;.cs</c> part in that feature's folder (one class, so every call stays <c>Timing.X</c>).
public static partial class Timing
{
    /// A program icon cache (<c>ProcessIcons</c>: the Mixer's, Network traffic control's) keeps the icons of at most this many programs (the ones asked for last).
    public const int ProcessIconCacheMax = 64;

    /// A window's undo history (<c>UndoHistory</c>) keeps at most this many steps; the oldest go first.
    public const int UndoLimit = 100;

    /// Changes of the same thing (a knob drag, typing a value) this close together are one undo step.
    public static readonly TimeSpan UndoMergeWindow = TimeSpan.FromSeconds(1);

    /// How long a pause ends the letters typed to jump to an entry of an open drop-down list.
    public static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

    /// After start-up, wait this long (pages built, pop-up warm-up drawn), then give the memory start-up needed back to Windows (one full collection).
    public static readonly TimeSpan CompactAfterStart = TimeSpan.FromSeconds(10);

    /// Once the main window has been hidden (or not yet shown) this long, the memory its use left behind is given back to Windows (its page views stay).
    public static readonly TimeSpan HiddenSettle = TimeSpan.FromSeconds(30);

    /// Once the main window has been hidden this long, it (with its page views) and the Actions and hotkeys window are let go (made again at
    /// their next showing, a little slower; the tray panel never is), then <see cref="UiReleaseCompactAfter"/> later one background collection gives their memory back.
    public static readonly TimeSpan UiReleaseAfterHidden = TimeSpan.FromMinutes(5);

    /// <inheritdoc cref="UiReleaseAfterHidden"/> The wait lets the closed windows' last frames and handles go first.
    public static readonly TimeSpan UiReleaseCompactAfter = TimeSpan.FromSeconds(2);

    /// This long after the main window is let go (its collection long done), timing-log.txt says whether the window is still in memory (something holds it).
    public static readonly TimeSpan UiReleaseLeakCheck = TimeSpan.FromSeconds(30);

    /// The process's memory goes to timing-log.txt this long after start-up (<c>UiTiming.Memory</c>; also at <see cref="HiddenSettle"/>).
    public static readonly TimeSpan MemoryLineAfterStart = TimeSpan.FromMinutes(2);

    /// The UI watchdog (<c>UiWatchdog</c>) posts a heartbeat this often; when the UI thread has not run one for <see cref="UiStuckAfter"/>,
    /// the stall goes to hang-log.txt, once per stall and at most <see cref="UiStuckLogsPerRun"/> times per run.
    public static readonly TimeSpan UiHeartbeatEvery = TimeSpan.FromSeconds(5), UiStuckAfter = TimeSpan.FromSeconds(2);

    /// <inheritdoc cref="UiHeartbeatEvery"/>
    public const int UiStuckLogsPerRun = 5;

    /// A diagnostics log that would grow past this many bytes is rolled over (kept as "name.old.txt", replacing the one before; <c>LogWriter</c>).
    public const long LogMaxBytes = 1_000_000;

    /// A setting change is written to its file this long after the last change of a burst (a slider drag saves once; <c>SaveSoon</c>).
    public static readonly TimeSpan SaveSoonDelay = TimeSpan.FromMilliseconds(500);

    /// "Show or hide OmniControl" ignores a request that comes this soon after the one before (a held hotkey's repeats arrive every ~30 ms
    /// after Windows' repeat delay of up to 1 s), so holding its key shows the window once instead of flipping it shown and hidden.
    public static readonly TimeSpan WindowToggleRepeat = TimeSpan.FromMilliseconds(1100);

    /// After Windows announces a hardware change (<see cref="HardwareWatches"/>), wait this long for the burst to end (a Remote Desktop return makes several) before refreshing what depends on it.
    public static readonly TimeSpan HardwareDebounce = TimeSpan.FromSeconds(2);

    /// After an announcement (and at start, then only when it looks wrong), a watch that can tell when it looks stale is refreshed at these
    /// times after the last one of the burst, stopping as soon as it looks right (<see cref="HardwareRetry"/>): after a return from Remote
    /// Desktop, an unlock or a log-on the monitors often do not answer DDC/CI for 2-10 s, and some take longer.
    public static readonly IReadOnlyList<TimeSpan> HardwareRetrySchedule =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40)];

    /// The periodic hardware check refreshes a watch that still looks stale at most this many times in a row; then it waits for the next announcement or Refresh.
    public const int HardwareStaleRefreshMax = 3;

    /// The choices of App settings' "Check hardware every" (<see cref="HardwareChecks"/>; 1 minute is the default): each look only compares what Windows lists with what the program holds and refreshes nothing unless that differs.
    public static readonly TimeSpan HardwareCheckMinute = TimeSpan.FromMinutes(1), HardwareCheck5Minutes = TimeSpan.FromMinutes(5), HardwareCheck15Minutes = TimeSpan.FromMinutes(15);

    /// One watched piece of hardware is refreshed at most this often, however many announcements arrive; a later one still gets its refresh once the gap is over.
    public static readonly TimeSpan HardwareRefreshGap = TimeSpan.FromSeconds(4);

    /// An action that finds its picked device missing makes its watch refresh once; the same watch is not refreshed again for an action within this time (a held key on an unplugged monitor).
    public static readonly TimeSpan HardwareOnDemandGap = TimeSpan.FromSeconds(10);

    /// How long the pointer rests on a sub-menu row before it opens, when Windows' own menu show delay cannot be read.
    public static readonly TimeSpan SubmenuDelay = TimeSpan.FromMilliseconds(300);

    /// Slide animation when a dragged list item makes room.
    public static readonly TimeSpan DragSlide = TimeSpan.FromMilliseconds(110);

    /// How long the preset manager offers "Undo" after presets were deleted.
    public static readonly TimeSpan PresetUndo = TimeSpan.FromSeconds(8);

    /// How long "Undo" stays beside a "Restore defaults" confirmation (the Easy page's and every other page's).
    public static readonly TimeSpan RestoreUndo = TimeSpan.FromSeconds(60);

    /// How long a "Restore defaults" line without an Undo ("Already at the defaults.") stays.
    public static readonly TimeSpan RestoreNote = TimeSpan.FromSeconds(6);

    /// How often the open page's "Restore defaults" button looks again at whether the page is at its defaults.
    public static readonly TimeSpan RestoreStateCheck = TimeSpan.FromSeconds(1);

    /// How long a restarted OmniControl waits for the run before it to end (it saves its settings as it closes) before it carries out a staged import or wipe.
    public static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(15);

    /// A key typed into a search box this soon after another waits this long before its search runs (the first letter is searched at once; <see cref="SearchRunner{T}"/>).
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(60);

    /// At most one automatic update check per this long (the daily check; "Check now" ignores it).
    public static readonly TimeSpan UpdateCheckEvery = TimeSpan.FromHours(24);

    /// The first automatic update check waits this long after start-up, so it never competes with the app settling.
    public static readonly TimeSpan UpdateCheckFirstDelay = TimeSpan.FromSeconds(45);

    /// How often the running app asks whether the daily update check is due (a PC that stays on for days).
    public static readonly TimeSpan UpdateCheckPoll = TimeSpan.FromHours(1);

    /// An update check that has not answered after this long counts as failed.
    public static readonly TimeSpan UpdateRequestTimeout = TimeSpan.FromSeconds(15);
}
