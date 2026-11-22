using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Traffic;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// Network Traffic Control's rules by place (<see cref="ScopedRules"/>): what Windows must hold for All adapters', an adapter's and a
/// network's own rules. Nothing touches Windows.
public class ScopedRulesTests
{
    private const string Wifi = "{11111111-1111-1111-1111-111111111111}", Lan = "{22222222-2222-2222-2222-222222222222}";
    private const string Home = "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", Hotspot = "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}", Office = "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}";
    private const string Game = @"C:\Games\game.exe";

    private static NetworkSnapshot Now(string wifiOn, NetworkCategory wifiCategory = NetworkCategory.Private, NetworkCategory lanCategory = NetworkCategory.Domain) =>
        new(new[] { new NetAdapter(Wifi, "Wi-Fi", AdapterKind.Wifi, true), new NetAdapter(Lan, "Ethernet", AdapterKind.Ethernet, true) },
            new[]
            {
                new SavedNetwork(Home, "Home-5G", wifiCategory, NetworkMedium.Wireless, false, wifiOn == Home ? new[] { Wifi } : Array.Empty<string>()),
                new SavedNetwork(Hotspot, "Phone hotspot", wifiCategory, NetworkMedium.Wireless, true, wifiOn == Hotspot ? new[] { Wifi } : Array.Empty<string>()),
                new SavedNetwork(Office, "Office LAN", lanCategory, NetworkMedium.Wired, false, new[] { Lan }),
            });

    private static AppNetRule Rule(int upload = 0, int download = 0, bool blocked = false) =>
        new() { Id = "game", Path = Game, UploadOn = upload > 0, UploadKbps = upload, DownloadOn = download > 0, DownloadKbps = download, Blocked = blocked };

    private static AppNetScope Own(string adapter, string network, params AppNetRule[] rules) =>
        new() { Adapter = adapter, AdapterName = adapter == Wifi ? "Wi-Fi" : "Ethernet", Network = network, Apps = rules.ToList() };

    /// Every place's own rules in effect (each one's automation rule holds).
    private static IReadOnlySet<NetScope> InEffect(ResourcesSetup setup) => setup.Scoped.Select(ScopedRules.Of).ToHashSet();

    [Fact]
    public void APlaceNotInEffect_UsesItsParents()
    {
        var setup = new ResourcesSetup { Apps = { Rule(download: 1024) }, Scoped = { Own(Wifi, Hotspot, Rule(download: 64)) } };
        Assert.Equal(1024L * 1024, ScopedRules.Wanted(setup, Now(Hotspot), new HashSet<NetScope>()).Rules.Single().DownloadBytes);
        Assert.Equal(64L * 1024, ScopedRules.Wanted(setup, Now(Hotspot), InEffect(setup)).Rules.Single(r => r.Adapter == Wifi).DownloadBytes);
    }

    [Fact]
    public void NoPlaceOfItsOwn_OneRuleEverywhere()
    {
        var setup = new ResourcesSetup { Apps = { Rule(upload: 512, blocked: true) } };
        var (rules, inexact) = ScopedRules.Wanted(setup, NetworkSnapshot.Empty, InEffect(setup));
        Assert.False(inexact);
        Assert.Equal(new[] { new NetworkRule("game", Game, 512UL * 1024 * 8, true) }, rules);
    }

    [Fact]
    public void AdapterBlocks_OnlyThatAdapterIsBlocked()
    {
        var setup = new ResourcesSetup { Scoped = { Own(Wifi, string.Empty, Rule(blocked: true)) } };
        var (rules, _) = ScopedRules.Wanted(setup, Now(Home), InEffect(setup));
        var block = Assert.Single(rules);
        Assert.True(block.Blocked);
        Assert.Equal((Wifi, "Wi-Fi"), (block.Adapter, block.AdapterName));
    }

    [Fact]
    public void NetworkOwnWins_OverItsAdapter()
    {
        var setup = new ResourcesSetup { Scoped = { Own(Wifi, string.Empty, Rule(download: 1024)), Own(Wifi, Hotspot, Rule(download: 64)) } };
        Assert.Equal(64L * 1024, ScopedRules.Wanted(setup, Now(Hotspot), InEffect(setup)).Rules.Single(r => r.Adapter == Wifi).DownloadBytes);
        Assert.Equal(1024L * 1024, ScopedRules.Wanted(setup, Now(Home), InEffect(setup)).Rules.Single(r => r.Adapter == Wifi).DownloadBytes);
    }

    [Fact]
    public void UploadDiffersByCategory_FollowsEachCategory()
    {
        var setup = new ResourcesSetup { Apps = { Rule(upload: 1024) }, Scoped = { Own(Wifi, string.Empty, Rule(upload: 64)) } };
        var (rules, inexact) = ScopedRules.Wanted(setup, Now(Home), InEffect(setup));
        Assert.False(inexact);
        Assert.Equal(64UL * 1024 * 8, rules.Single(r => r.Profile == NetworkCategory.Private).UploadBits);
        Assert.Equal(1024UL * 1024 * 8, rules.Single(r => r.Profile == NetworkCategory.Domain).UploadBits);
    }

    [Fact]
    public void UploadDiffersWithinOneCategory_UsesAllAdaptersAndSaysSo()
    {
        var setup = new ResourcesSetup { Apps = { Rule(upload: 1024) }, Scoped = { Own(Wifi, string.Empty, Rule(upload: 64)) } };
        var (rules, inexact) = ScopedRules.Wanted(setup, Now(Home, NetworkCategory.Private, NetworkCategory.Private), InEffect(setup));
        Assert.True(inexact);
        Assert.Equal(1024UL * 1024 * 8, Assert.Single(rules).UploadBits);
    }

    [Fact]
    public void RulesOf_NetworkWithoutItsOwn_UsesItsAdapters()
    {
        var adapter = Own(Wifi, string.Empty, Rule(download: 1024));
        var setup = new ResourcesSetup { Apps = { Rule(download: 64) }, Scoped = { adapter } };
        Assert.Same(adapter.Apps, ScopedRules.RulesOf(setup, new NetScope(Wifi, Home)));
        Assert.Same(setup.Apps, ScopedRules.RulesOf(setup, NetScope.Of(Lan)));
    }
}
