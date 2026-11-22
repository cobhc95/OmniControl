using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// The network rules' sync decision (<see cref="NetworkRules.Plan"/>): what Windows holds against what is wanted. Nothing touches Windows.
public class NetworkRulesPlanTests
{
    private const string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    private const ulong Kb512 = 512 * 1024 * 8, Kb256 = 256 * 1024 * 8;

    private static HeldRule Limit(ulong bits, string name = "OmniControl limit msedge") => new(name, "msedge.exe", bits);
    private static HeldRule Block(bool inbound) => new($"OmniControl block msedge ({(inbound ? "in" : "out")})", Edge, Inbound: inbound);
    private static NetworkRule Rule(ulong? upload = null, bool blocked = false, bool uploadBlocked = false) => new("msedge", Edge, upload, blocked, UploadBlocked: uploadBlocked);

    [Fact]
    public void NothingHeld_LimitWanted_IsAdded()
    {
        var plan = NetworkRules.Plan(new[] { Rule(Kb512) }, Array.Empty<HeldRule>());
        Assert.Empty(plan.Remove);
        Assert.Equal(new[] { Limit(Kb512) }, plan.Add);
    }

    [Fact]
    public void LimitCleared_PolicyIsRemoved()
    {
        var plan = NetworkRules.Plan(Array.Empty<NetworkRule>(), new[] { Limit(Kb512) });
        Assert.Equal(new[] { Limit(Kb512) }, plan.Remove);
        Assert.Empty(plan.Add);
    }

    [Fact]
    public void LimitChanged_OldGoesNewComes()
    {
        var plan = NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb256) });
        Assert.Equal(new[] { Limit(Kb256) }, plan.Remove);
        Assert.Equal(new[] { Limit(Kb512) }, plan.Add);
    }

    [Fact]
    public void SameRuleHeld_NothingChanges() =>
        Assert.True(NetworkRules.Plan(new[] { Rule(Kb512, blocked: true) }, new[] { Limit(Kb512), Block(true), Block(false) }).None);

    [Fact]
    public void RateRoundedByWindows_CountsAsSame() =>
        Assert.True(NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb512 / 8000 * 8000) }).None);

    [Fact]
    public void NameInOtherCase_CountsAsSame() =>
        Assert.True(NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb512, "omnicontrol LIMIT MSEDGE") }).None);

    [Fact]
    public void DuplicatePolicy_IsRemovedAndPutBackOnce()
    {
        var plan = NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb512), Limit(Kb256) });
        Assert.Single(plan.Remove);
        Assert.Equal(new[] { Limit(Kb512) }, plan.Add);
    }

    [Fact]
    public void LeftOverOfAnotherRun_IsRemoved()
    {
        var plan = NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb512), Limit(Kb256, "OmniControl limit chrome") });
        Assert.Equal(new[] { Limit(Kb256, "OmniControl limit chrome") }, plan.Remove);
        Assert.Empty(plan.Add);
    }

    [Fact]
    public void BlockCleared_BothDirectionsGo()
    {
        var plan = NetworkRules.Plan(new[] { Rule(Kb512) }, new[] { Limit(Kb512), Block(true), Block(false) });
        Assert.Equal(new[] { Block(true), Block(false) }, plan.Remove);
        Assert.Empty(plan.Add);
    }

    [Fact]
    public void HalfABlock_MissingDirectionIsAdded()
    {
        var plan = NetworkRules.Plan(new[] { Rule(blocked: true) }, new[] { Block(true) });
        Assert.Empty(plan.Remove);
        Assert.Equal(new[] { Block(false) }, plan.Add);
    }

    [Fact]
    public void BlockOfAnotherPath_IsReplaced()
    {
        var moved = Block(false) with { Program = @"D:\Old\msedge.exe" };
        var plan = NetworkRules.Plan(new[] { Rule(blocked: true) }, new[] { Block(true), moved });
        Assert.Equal(new[] { moved }, plan.Remove);
        Assert.Equal(new[] { Block(false) }, plan.Add);
    }

    [Fact]
    public void UploadBlock_IsTheOutboundRuleAlone()
    {
        var plan = NetworkRules.Plan(new[] { Rule(uploadBlocked: true) }, Array.Empty<HeldRule>());
        Assert.Equal(new[] { Block(false) }, plan.Add);
    }

    [Fact]
    public void UploadBlockCleared_OutboundRuleGoes_FullBlockKeepsIt()
    {
        Assert.Equal(new[] { Block(false) }, NetworkRules.Plan(Array.Empty<NetworkRule>(), new[] { Block(false) }).Remove);
        Assert.True(NetworkRules.Plan(new[] { Rule(blocked: true, uploadBlocked: true) }, new[] { Block(true), Block(false) }).None);
    }

    [Fact]
    public void UploadBlockOnToFullBlock_AddsOnlyTheInboundRule()
    {
        var plan = NetworkRules.Plan(new[] { Rule(blocked: true) }, new[] { Block(false) });
        Assert.Empty(plan.Remove);
        Assert.Equal(new[] { Block(true) }, plan.Add);
    }

    [Fact]
    public void DownloadBlock_NeedsNoFirewallRule() => Assert.False(new NetworkRule("msedge", Edge, null, false, DownloadBlocked: true).NeedsRules);
}
