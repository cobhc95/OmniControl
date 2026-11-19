namespace OmniControl.App.Shared.Services;

// The intervals and limits of mouse buttons, gestures, rules and the trackpad; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// On quit, how long to wait for queued mouse work (letting go of held keys and buttons, restoring the pointer) to finish.
    public static readonly TimeSpan MouseQueueDrain = TimeSpan.FromSeconds(1);

    /// The web search action sends Ctrl+C, then waits this long for the program to put the selection on the clipboard.
    public static readonly TimeSpan MouseCopyWait = TimeSpan.FromMilliseconds(150);

    /// The pause between one action and the next when a button or gesture has several, so a program has taken the first keystroke
    /// or window change before the next one arrives.
    public static readonly TimeSpan MouseActionGap = TimeSpan.FromMilliseconds(40);

    /// Repeated clicking: the hard ceiling on its speed, whatever is set (clicks per second).
    public const int ClickSpeedMax = 50;

    /// Repeated clicking: how often the loop looks at Esc, the pointer and the stop limits while it waits between clicks.
    public static readonly TimeSpan ClickWatchEvery = TimeSpan.FromMilliseconds(10);

    /// Repeated clicking: how far the pointer may drift (pixels) before "stop when the mouse moves" stops it.
    public const int ClickMoveTolerance = 4;

    /// How long a mouse button waits for the one action gate's answer from the UI thread before the action is refused (the hook must never wait long).
    public static readonly TimeSpan GateAskWait = TimeSpan.FromMilliseconds(150);

    /// Repeated clicking: how often the on-screen badge is refreshed with the click count.
    public static readonly TimeSpan ClickBadgeEvery = TimeSpan.FromSeconds(1);

    /// While the Mouse and Trackpad page shows the gesture areas on screen, how often they are looked up again (the active window's title bar moves).
    public static readonly TimeSpan GestureMarginsRefresh = TimeSpan.FromMilliseconds(250);

    /// How long an input's card on the Mouse and Trackpad page stays lit after the real button is pressed or the gesture recognised.
    public static readonly TimeSpan MouseFlash = TimeSpan.FromMilliseconds(180);

    /// Least time between two recognised trackpad gestures (a bouncing finger or a quick second touch is not a new gesture).
    public static readonly TimeSpan TrackpadGestureCooldown = TimeSpan.FromMilliseconds(300);

    /// The backstop look at Windows' touchpad settings (a few reads, no writes unless something turned a gesture OmniControl keeps off back
    /// on); a write to the settings key is seen at once without waiting for it, so this is only the safety net.
    public static readonly TimeSpan TrackpadTakeoverCheck = TimeSpan.FromSeconds(45);

    /// While the takeover's safety net watches for a panel Windows opened after a gesture (for <c>CoreTiming.TrackpadSurfaceWatchMs</c>),
    /// it looks at the window in front this often: short enough that a panel is closed before it has finished opening.
    public static readonly TimeSpan TrackpadSurfaceLook = TimeSpan.FromMilliseconds(20);

    /// At most this many recognised-gesture lines go to the trackpad log per window (takeover lines are never limited).
    public static readonly TimeSpan TrackpadLogGestureWindow = TimeSpan.FromMinutes(1);

    public const int TrackpadLogGestureLimit = 30;

    /// After OmniControl swaps the primary and secondary mouse buttons, the swap is put back unless "Keep" is chosen within this time,
    /// so a swap the user can't work with (or didn't mean) never stays.
    public static readonly TimeSpan PointerSwapKeep = TimeSpan.FromSeconds(15);

    /// A mouse button may start at most <see cref="LaunchBurstMax"/> programs or folders within this time; more is treated as a runaway
    /// (a repeating or looping mapping) and refused, so it can never flood the desktop with windows.
    public static readonly TimeSpan LaunchBurstWindow = TimeSpan.FromSeconds(5);

    public const int LaunchBurstMax = 3;

    /// A custom gesture rule runs at most once per this time unless its own rate limit says otherwise (the default of a new rule).
    public static readonly TimeSpan RuleRateLimit = TimeSpan.FromSeconds(1);

    /// The rate limits a rule's editor offers (its drop-down), shortest first; <see cref="RuleRateLimit"/> is one of them.
    public static readonly IReadOnlyList<TimeSpan> RuleRateLimits =
        new[] { 100, 250, 500, 1000, 2000, 5000, 10000 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray();

    /// "Press and hold" triggers: how long the button must be held, unless the trigger's own setting says otherwise.
    public static readonly TimeSpan RuleHold = TimeSpan.FromMilliseconds(600);

    /// A rule's command is stopped (with everything it started) after this long, unless the step's own timeout says otherwise.
    public static readonly TimeSpan RuleCommandTimeout = TimeSpan.FromSeconds(30);

    /// A rule's "Wait" step waits this long unless set otherwise.
    public static readonly TimeSpan RuleWait = TimeSpan.FromMilliseconds(500);

    /// After a wake or a log-in, the waits between readiness checks (network up, device answers); the last repeats.
    public static readonly IReadOnlyList<TimeSpan> ReadyBackoff =
        new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(20) };

    /// The longest a step waits for the network and its device before it is reported as not reachable.
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    /// The waits before a failed step is tried again; the last repeats.
    public static readonly IReadOnlyList<TimeSpan> StepRetryBackoff = new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) };

    /// How many times in all a step that needs a device is tried.
    public const int StepTriesMax = 3;
}
