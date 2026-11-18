using OmniControl.App.Osd;
using Xunit;

namespace OmniControl.Tests;

public class OsdDurationTests
{
    private static OsdDurationRule App(string id, double s) => new() { Kind = OsdDurationKind.App, Key = id, Name = id, Seconds = s };
    private static OsdDurationRule Event(string name, double s) => new() { Kind = OsdDurationKind.Event, Key = name, Name = name, Seconds = s };

    [Fact]
    public void No_exception_matches_gives_everything_else() =>
        Assert.Equal(TimeSpan.FromSeconds(2), OsdDuration.For(2, new[] { App("spotify", 5) }, "Volume", "chrome"));

    [Fact]
    public void The_first_match_in_list_order_wins()
    {
        var rules = new[] { Event("Volume", 4), App("spotify", 5) };
        Assert.Equal(TimeSpan.FromSeconds(4), OsdDuration.For(2, rules, "Volume", "spotify"));
        Assert.Equal(TimeSpan.FromSeconds(5), OsdDuration.For(2, rules, "Track and artist", "spotify"));
    }

    [Fact]
    public void An_unknown_program_matches_event_exceptions_only()
    {
        var rules = new[] { App("", 9), App("spotify", 5), Event("Smart devices", 4) };
        Assert.Equal(TimeSpan.FromSeconds(2), OsdDuration.For(2, rules, "Volume", null));
        Assert.Equal(TimeSpan.FromSeconds(4), OsdDuration.For(2, rules, "Smart devices", ""));
    }

    [Fact]
    public void Times_are_held_within_the_limits() =>
        Assert.Equal(TimeSpan.FromSeconds(OsdDuration.MaxSeconds), OsdDuration.For(99, Array.Empty<OsdDurationRule>(), "Volume", null));
}
