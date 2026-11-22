using OmniControl.App.Features.Resources.Gadget;

namespace OmniControl.Tests;

/// The gadget's double-click pairing rule: only the time counts, never where on the gadget the second press lands.
public class GadgetClicksTests
{
    private static readonly TimeSpan Time = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void SecondPress_WithinTime_Pairs() => Assert.True(GadgetClicks.Pairs(TimeSpan.FromMilliseconds(200), Time));

    [Fact]
    public void SecondPress_AtTheLimit_Pairs() => Assert.True(GadgetClicks.Pairs(Time, Time));

    [Fact]
    public void SecondPress_AfterTime_DoesNotPair() => Assert.False(GadgetClicks.Pairs(Time + TimeSpan.FromMilliseconds(1), Time));
}
