using OmniControl.App.Features.OtherPcs;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// "Can't reach it on this network?": the live rows follow what is known, the reminders and the firewall's port list read as Windows writes them.
public class ReachChecksTests
{
    [Fact]
    public void PublicNetwork_FailsWithItsFix_AndPrivatePasses()
    {
        var failing = NetworkChecks.ForPairedPc(false, true).ThisPc;
        Assert.Equal(CheckState.Failed, failing[0].State);
        Assert.Equal("Make it Private", failing[0].Fix);
        Assert.Equal(CheckState.Ok, failing[1].State);
        Assert.Equal(CheckState.Ok, NetworkChecks.ForPairing(true).ThisPc[0].State);
    }

    [Fact]
    public void UnknownLiveRows_AreLeftOut_ButTheRemindersStay()
    {
        var checks = NetworkChecks.ForPairedPc(null, null);
        Assert.Empty(checks.ThisPc);
        Assert.Equal(3, checks.OtherPc.Count);
        Assert.All(checks.OtherPc, c => Assert.Equal(CheckState.Pending, c.State));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("", true)]
    [InlineData("80,47810", true)]
    [InlineData("47000-48000", true)]
    [InlineData("80,443", false)]
    [InlineData("RPC", false)]
    public void PortListed_ReadsWindowsPortLists(string ports, bool listed) => Assert.Equal(listed, PcFirewall.PortListed(ports, 47810));
}
