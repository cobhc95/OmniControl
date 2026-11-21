using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Shared.Services;

// The intervals of the Resources page, its taskbar gadget and its network limits; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How often the readings are taken while the page is open or a rule follows one (the Live readings chip's default).
    public static readonly TimeSpan ResourcesReadEvery = TimeSpan.FromSeconds(2);

    /// How often the taskbar gadget reads (its own setting's default).
    public static readonly TimeSpan ResourcesGadgetReadEvery = TimeSpan.FromSeconds(1);

    /// The read intervals offered by the page's Live readings chip, in seconds.
    public static readonly IReadOnlyList<double> ResourcesReadChoices = new[] { 0.5, 1, 2, 5, 10 };

    /// The gadget's "Refresh every": the shortest and longest that can be typed, the quick choices, and below what it notes the CPU cost.
    public static readonly TimeSpan ResourcesGadgetReadMin = TimeSpan.FromMilliseconds(100), ResourcesGadgetReadMax = TimeSpan.FromSeconds(10),
        ResourcesGadgetReadCostly = TimeSpan.FromMilliseconds(500);
    public static readonly IReadOnlyList<TimeSpan> ResourcesGadgetQuickReads = new[] { 250, 500, 1000, 2000 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToList();

    /// On Left, the least time between two looks at where the taskbar's left buttons end (UI Automation into Explorer, at the gadget's reads):
    /// a program button that came or went is followed within this.
    public static readonly TimeSpan ResourcesGadgetLeftLook = TimeSpan.FromSeconds(5);

    /// The gadget's weather: the default, shortest and longest interval, one request's limit, and the pause after typing before a place search.
    public static readonly TimeSpan WeatherUpdateEvery = TimeSpan.FromMinutes(30), WeatherUpdateMin = TimeSpan.FromMinutes(5),
        WeatherUpdateMax = TimeSpan.FromHours(3), WeatherRequestTimeout = TimeSpan.FromSeconds(15), WeatherSearchPause = TimeSpan.FromMilliseconds(500);

    /// After a weather request that could not get through (no network yet at sign-in, just after waking), the first retry; each next one waits twice as long, up to the update interval.
    public static readonly TimeSpan WeatherRetryFirst = TimeSpan.FromSeconds(30);

    /// With no sensor reading needed for this long the sensor sources close (the sensor library's memory and driver handles back); the next need opens it again.
    public static readonly TimeSpan ResourcesIdleClose = TimeSpan.FromSeconds(60);

    /// How many readings each sparkline shows.
    public const int ResourcesHistory = 60;

    /// The least time between two changes of Windows' network rules: a burst of chip clicks or actions is applied once.
    public static readonly TimeSpan ResourcesRulesGap = TimeSpan.FromSeconds(1);

    /// Download limits: burst, longest hold (then TCP slows), port table age while a limited program owns ports and while none does, stop wait.
    public static readonly ShapingTimes ResourcesShaping = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

    /// The network service stops itself after this long with no client, and ends its traffic session after this long with no read.
    public static readonly TimeSpan NetworkServiceIdle = TimeSpan.FromSeconds(60), NetworkServiceSpeedsIdle = TimeSpan.FromSeconds(30);

    /// How long OmniControl waits for the network service to take the connection after asking Windows to start it, for one answer
    /// (a change of Windows' rules can take a few seconds), and for it to stop when it is removed.
    public static readonly TimeSpan NetworkServiceConnectWait = TimeSpan.FromSeconds(5), NetworkServiceReplyWait = TimeSpan.FromSeconds(20),
        NetworkServiceStopWait = TimeSpan.FromSeconds(15);
}
