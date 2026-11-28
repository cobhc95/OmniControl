namespace OmniControl.App.Shared.Services;

// The intervals and limits of automations, power actions and the power journal; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How often the automation countdown window counts down (it shows whole seconds).
    public static readonly TimeSpan CountdownTick = TimeSpan.FromSeconds(1);

    /// How often the automations look at the clock, the battery, the idle time, the running programs and the devices.
    public static readonly TimeSpan AutomationTick = TimeSpan.FromSeconds(5);

    /// How often that look runs while no rule is on (it then only keeps the power states fresh).
    public static readonly TimeSpan AutomationIdleTick = TimeSpan.FromSeconds(60);

    /// The battery charge (percent) at or below which the Power header's battery fact turns to the caution colour.
    public const int BatteryLowPercent = 20;

    /// How often the clock check samples (a gap of <see cref="PowerClockGap"/> is a stop, so a slow sample costs nothing in what is found).
    public static readonly TimeSpan PowerClockTick = TimeSpan.FromSeconds(30);

    /// A clock sample this much later than asked means the program was stopped (the PC slept, or Modern Standby froze it): one piece of wake evidence.
    public static readonly TimeSpan PowerClockGap = TimeSpan.FromSeconds(20);

    /// A wake told by one channel waits this long for a second one (<see cref="Core.Engine.WakeDetector"/>).
    public static readonly TimeSpan WakeConfirmWindow = TimeSpan.FromSeconds(20);

    /// A waiting wake candidate is decided this long after the last power event (once its confirm window surely passed); one look, not a tick.
    public static readonly TimeSpan WakeDecideAfter = WakeConfirmWindow + TimeSpan.FromSeconds(1);

    /// After a confirmed wake, more wake evidence within this time is the same wake told again, unless the PC went down in between.
    public static readonly TimeSpan WakeQuiet = TimeSpan.FromMinutes(2);

    /// The display must have been off at least this long for its return to count as wake evidence (Modern Standby; a short blank is not).
    public static readonly TimeSpan StandbyDisplayOffMin = TimeSpan.FromSeconds(30);

    /// The same kind of power event told by several channels within this time is one journal entry.
    public static readonly TimeSpan PowerEventSame = TimeSpan.FromSeconds(5);

    /// "At log in" runs once per sign-in: a start-up whose Windows start time is within this of the last one that ran it, in the same session, is the same sign-in.
    public static readonly TimeSpan LoginSameBoot = TimeSpan.FromMinutes(2);

    /// A time-of-day automation whose time passed while the PC slept still runs if it woke within this time of it.
    public static readonly TimeSpan AutomationCatchUp = TimeSpan.FromMinutes(10);

    /// An automation never starts again within this time of its last start.
    public static readonly TimeSpan AutomationMinGap = TimeSpan.FromSeconds(10);

    /// An automation that starts more than <see cref="AutomationBurstMax"/> times within this window is paused (it is looping).
    public static readonly TimeSpan AutomationBurstWindow = TimeSpan.FromMinutes(1);

    public const int AutomationBurstMax = 5;

    /// The most rules one chain may hold: a rule that runs an action (or changes a state) that starts another rule, and so on. The rule
    /// that would make the chain longer is not started, and a rule never starts from its own chain (no loops).
    public const int AutomationChainMax = 3;

    /// The longest "then wait" between two actions of an automation, in seconds, and what a new one starts at.
    public const int AutomationMaxWaitSeconds = 600, AutomationDefaultWaitSeconds = 5;

    /// A rule that a trigger started (not a person) may not sleep, hibernate, shut down, restart or sign out within this time of OmniControl starting or the PC waking.
    public static readonly TimeSpan PowerActionGrace = TimeSpan.FromMinutes(2);

    /// The countdown (with Cancel) before sleep, hibernate, shut down, restart or sign out.
    public static readonly TimeSpan PowerCountdown = TimeSpan.FromSeconds(20);

    /// The shortest and longest countdown an input may ask for (never none: the user must always be able to cancel).
    public const int PowerCountdownMinSeconds = 5, PowerCountdownMaxSeconds = 300;

    /// The countdown a power action really uses: the one asked for, held within the limits, or <see cref="PowerCountdown"/> when none was.
    public static TimeSpan PowerCountdownOf(double? seconds) =>
        seconds is { } asked ? TimeSpan.FromSeconds(Math.Clamp(asked, PowerCountdownMinSeconds, PowerCountdownMaxSeconds)) : PowerCountdown;

    /// A custom action's command or script is stopped (with everything it started) after this long.
    public static readonly TimeSpan CustomActionTimeout = TimeSpan.FromSeconds(60);
}
