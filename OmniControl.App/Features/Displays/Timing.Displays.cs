namespace OmniControl.App.Shared.Services;

// The intervals and limits of the displays; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// A display level OmniControl set or read this recently is trusted for the next step instead of asking the display again (DDC/CI reads take 40 ms or more).
    public static readonly TimeSpan DisplayLevelTrust = TimeSpan.FromSeconds(10);

    /// The displays are looked at again (to see whether one was plugged in or removed) at most this often, however many events ask for it.
    public static readonly TimeSpan DisplayRescanGap = TimeSpan.FromSeconds(4);

    /// How often the warm-light schedule looks at the clock; its ease in and out is 30 minutes, so every look moves the picture by a small step.
    public static readonly TimeSpan WarmLightTick = TimeSpan.FromSeconds(30);

    /// "Turn off displays" waits this long first, so the click or key that started it does not wake the screens again at once.
    public static readonly TimeSpan DisplaysOffGrace = TimeSpan.FromSeconds(1);
}
