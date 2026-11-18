using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>What an exception of the OSD duration matches: the program that caused the pop-up, or the thing shown (an <see cref="OsdElement"/>).</summary>
public enum OsdDurationKind { App, Event }

/// <summary>One exception of the OSD duration (docs/mockups/osd-duration.html), kept in osd.json in the user's order.</summary>
public sealed class OsdDurationRule
{
    public OsdDurationKind Kind { get; set; }

    /// <summary>The program's identity (<see cref="Core.Engine.ProgramIdentity.Normalize"/>) for an app, the element's name for an event.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The name a person knows the program by (an event's name is its key).</summary>
    public string Name { get; set; } = string.Empty;

    public double Seconds { get; set; } = OsdDuration.Default;

    /// <summary>What the exception's chip says: "App: Spotify", "Event: Volume".</summary>
    public string Describe() => Kind == OsdDurationKind.App ? $"App: {Name}" : $"Event: {Key}";
}

/// <summary>
/// How long the OSD stays: the limits of a duration and THE resolver (pure): the first exception that matches the pop-up, in list order,
/// gives its time, else "Everything else". A pop-up whose program is unknown is matched by event exceptions only.
/// </summary>
public static class OsdDuration
{
    /// <summary>The time with no setting (the pop-up's time before it could be set), and the limits and decimals of a typed or slid time.</summary>
    public static readonly double Default = Timing.OsdVisible.TotalSeconds;
    public const double MinSeconds = 0.5, MaxSeconds = 10;
    public const int Decimals = 1;

    public static double Clamp(double seconds) =>
        double.IsFinite(seconds) ? Math.Round(Math.Clamp(seconds, MinSeconds, MaxSeconds), Decimals) : Default;

    /// <summary>How long the pop-up of <paramref name="element"/> caused by <paramref name="program"/> (null or empty: unknown) stays.</summary>
    public static TimeSpan For(double everythingElse, IReadOnlyList<OsdDurationRule> rules, string element, string? program)
    {
        foreach (var rule in rules)
            if (Matches(rule, element, program)) return TimeSpan.FromSeconds(Clamp(rule.Seconds));
        return TimeSpan.FromSeconds(Clamp(everythingElse));
    }

    private static bool Matches(OsdDurationRule rule, string element, string? program) => rule.Kind switch
    {
        OsdDurationKind.App => !string.IsNullOrEmpty(program) && rule.Key == program,
        _ => rule.Key == element,
    };
}
