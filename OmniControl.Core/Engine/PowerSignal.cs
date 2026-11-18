namespace OmniControl.Core.Engine;

/// <summary>A power or session event the power journal records.</summary>
public enum PowerEventKind
{
    Suspend,
    Resume,
    Hibernate,
    Lock,
    Unlock,
    Logon,
    Logoff,
    Shutdown,

    /// <summary>The console's display went off (on a Modern Standby PC this is how standby starts).</summary>
    DisplayOff,

    /// <summary>The console's display came back on.</summary>
    DisplayOn,

    /// <summary>The console's display dimmed (still showing, at low brightness).</summary>
    DisplayDimmed,

    /// <summary>The clock check found that the program was stopped for a while (the PC slept, or Modern Standby froze it).</summary>
    ClockGap,

    /// <summary>Windows started (found at start-up, from the time since boot).</summary>
    Boot,
}

/// <summary>
/// Which independent listener told an event. A wake is confirmed by two different channels (<see cref="WakeDetector"/>): the hidden window
/// and <c>SystemEvents</c> each hear Windows' broadcast on their own thread, the display notice and the clock check are separate evidence,
/// and the event log is read at start-up and after a wake.
/// </summary>
public enum PowerChannel { Window, SystemEvents, Display, Clock, EventLog, Uptime }

/// <summary>What Windows last said about the console's screens as a whole (whoever turned them off); Unknown until it has said anything.</summary>
public enum ScreenLight { Unknown, On, Dimmed, Off }

/// <summary>One event as a listener told it, with a short detail for the log ("PBT_APMRESUMEAUTOMATIC", "slept 8 min").</summary>
public sealed record PowerSignal(PowerEventKind Kind, PowerChannel Channel, string Detail);
