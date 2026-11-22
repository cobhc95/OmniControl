using OmniControl.App.Features.OtherPcs;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// "Can't reach it on this network?": the live rows follow what is known, the reminders and the firewall's port list read as Windows writes them.
public class ReachChecksTests
{
    private static readonly ReachFacts Fine = new(NetworkReach.Private, FirewallReach.Allowed, true, string.Empty);

    private static CheckRow Only(ReachFacts facts, string words) => Assert.Single(NetworkChecks.ForPairedPc(facts, "log.txt").ThisPc, r => r.Words.Contains(words));

    [Fact]
    public void AllFine_TwoTicks_AndTheReminders()
    {
        var checks = NetworkChecks.ForPairedPc(Fine, "log.txt");
        Assert.Equal(2, checks.ThisPc.Count);
        Assert.All(checks.ThisPc, r => Assert.Equal(CheckState.Ok, r.State));
        Assert.Equal(3, checks.OtherPc.Count);
        Assert.All(checks.OtherPc, c => Assert.Equal(CheckState.Pending, c.State));
    }

    [Theory]
    [InlineData(NetworkReach.Public, CheckState.Failed, "Make it Private")]
    [InlineData(NetworkReach.None, CheckState.Failed, "Open network settings")]
    [InlineData(NetworkReach.Unknown, CheckState.Pending, "Open network settings")]
    [InlineData(NetworkReach.Failed, CheckState.Pending, "")]
    public void NetworkRow_FollowsWhatIsKnown(NetworkReach network, CheckState state, string fix)
    {
        var row = NetworkChecks.ForPairedPc(Fine with { Network = network }, "log.txt").ThisPc[0];
        Assert.Equal(state, row.State);
        Assert.Equal(fix, row.Fix);
    }

    [Fact]
    public void NotReadYet_IsLeftOut_NotShownAsAFailure()
    {
        var rows = NetworkChecks.ForPairedPc(Fine with { Network = NetworkReach.NotRead, Firewall = FirewallReach.NotRead }, "log.txt").ThisPc;
        Assert.Empty(rows);
    }

    [Fact]
    public void Firewall_NoRuleOffersThePill_BlockSaysWhy_FailedPointsAtTheLog()
    {
        var noRule = Only(Fine with { Firewall = FirewallReach.NoRule }, "Firewall");
        Assert.Equal((CheckState.Failed, "Allow on Private networks", true), (noRule.State, noRule.Fix, noRule.PillFix));
        var blocked = Only(Fine with { Firewall = FirewallReach.Blocked }, "blocks");
        Assert.Equal((CheckState.Failed, string.Empty), (blocked.State, blocked.Fix));
        Assert.NotEmpty(blocked.Sentence);
        var failed = Only(Fine with { Firewall = FirewallReach.Failed }, "Couldn't check Windows Firewall");
        Assert.Equal(CheckState.Pending, failed.State);
        Assert.Contains("log.txt", failed.TagTip);
    }

    [Fact]
    public void PortsTaken_OtherVersion_AndLostKey_EachFail()
    {
        Assert.Equal(CheckState.Failed, Only(Fine with { PortsHeld = false }, "ports").State);
        Assert.Equal(CheckState.Failed, Only(Fine with { OtherVersion = "Desk" }, "Desk runs a different").State);
        Assert.Equal(CheckState.Failed, Only(Fine with { KeyMissing = true }, "pairing key").State);
    }

    [Fact]
    public void Pairing_HasTheFirewallRow_ButNoKeyRow()
    {
        var rows = NetworkChecks.ForPairing(Fine with { Firewall = FirewallReach.NoRule, KeyMissing = true }, "log.txt").ThisPc;
        Assert.Equal(2, rows.Count);
        Assert.Equal(NetworkChecks.Firewall, rows[1].Id);
    }

    [Fact]
    public void OtherVersionBeacon_NamesItsPc_AndIsNotHeardAsAPc()
    {
        var named = string.Empty;
        var json = System.Text.Encoding.UTF8.GetBytes($$"""{"M":"{{PcProtocol.BeaconPrefix}}1","Id":"{{new string('a', PcProtocol.IdBytes * 2)}}","Name":"Desk","Port":47810,"Pairing":false}""");
        Assert.Null(PcDiscovery.Read(json, n => named = n));
        Assert.Equal("Desk", named);
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
