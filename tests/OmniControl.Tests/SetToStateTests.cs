using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.Tests;

/// <summary>"Set to state": an On or Off action does nothing when its thing is already there (HotkeyRegistry.SkipsOrWaitsForState).</summary>
public class SetToStateTests
{
    private sealed class FakeStates : IStateReader
    {
        public StateValue? Value;
        public bool CanRefresh;
        public StateValue? AfterRefresh;
        public int Refreshes;

        public StateValue? Read(string id) => Value;

        public bool Refresh(string id, Action done)
        {
            if (!CanRefresh) return false;
            Refreshes++;
            Value = AfterRefresh;
            done();
            return true;
        }
    }

    private static HotkeyAction Action(string role) =>
        new("t." + role, "Orbit: " + role, "Smart devices", "", _ => { }) { Family = new ActionFamily("t", "Orbit"), FamilyRole = role, StateId = "t.power" };

    private static (HotkeyRegistry Registry, List<string> Said, List<string> Ran) Setup(FakeStates states)
    {
        var registry = new HotkeyRegistry(new HardwareWatches()) { StateReader = states };
        var said = new List<string>();
        registry.Report = said.Add;
        return (registry, said, new List<string>());
    }

    [Theory]
    [InlineData("On", true, true)]
    [InlineData("Off", false, true)]
    [InlineData("On", false, false)]
    [InlineData("Off", true, false)]
    public void KnownState_SkipsOnlyWhenAlreadyThere(string role, bool isOn, bool skipped)
    {
        var (registry, said, _) = Setup(new FakeStates { Value = StateValue.Of(isOn) });
        using var _ = registry;

        Assert.Equal(skipped, registry.SkipsOrWaitsForState(Action(role), null, _ => { }, stateRead: false));
        Assert.Equal(skipped, said.Count == 1);
    }

    [Fact]
    public void Unknown_NotRefreshable_RunsAndSaysSo()
    {
        var (registry, said, _) = Setup(new FakeStates());
        using var _ = registry;

        Assert.False(registry.SkipsOrWaitsForState(Action("On"), null, _ => { }, stateRead: false));
        Assert.Single(said);
    }

    [Fact]
    public void Unknown_Refreshable_WaitsThenDecidesFromTheFreshState()
    {
        var states = new FakeStates { CanRefresh = true, AfterRefresh = StateValue.Of(true) };
        var (registry, said, _) = Setup(states);
        using var _ = registry;
        var action = Action("On");
        var reran = 0;

        // The refresh hands the run back once; the second look sees "on" and skips.
        bool Check(bool read) => registry.SkipsOrWaitsForState(action, null, _ => { }, read);
        Assert.True(Check(false)); // refresh requested (the fake calls done at once, which the real registry posts to RunNow)
        Assert.Equal(1, states.Refreshes);
        Assert.True(Check(true));
        reran++;
        Assert.Equal(1, reran);
        Assert.Contains("already on", said.Last());
    }

    [Fact]
    public void ToggleAndUnlinkedActions_AlwaysRun()
    {
        var (registry, said, _) = Setup(new FakeStates { Value = StateValue.Of(true) });
        using var _ = registry;
        var unlinked = new HotkeyAction("t.x", "x", "c", "", _ => { }) { Family = new ActionFamily("t", "t"), FamilyRole = "On" };

        Assert.False(registry.SkipsOrWaitsForState(Action("Toggle"), null, _ => { }, false));
        Assert.False(registry.SkipsOrWaitsForState(unlinked, null, _ => { }, false));
        Assert.Empty(said);
    }

    [Fact]
    public void OnOffFamily_MakesOnAndOffNextToTheToggle_KeepingItsId()
    {
        var family = new OnOffFamily("volume.mute", "Mute", "volume.master.muted");
        var toggle = new HotkeyAction("volume.mute", "Toggle mute", "Volume", "mute", _ => { }) { Family = family.Family, FamilyRole = OnOffFamily.ToggleRole };
        var ran = new List<bool>();

        var all = family.Siblings(toggle, ran.Add).Prepend(toggle).ToList();

        Assert.Equal(new[] { "volume.mute", "volume.mute.on", "volume.mute.off" }, all.Select(a => a.Id));
        Assert.Equal(new[] { "Toggle", "On", "Off" }, all.Select(a => a.FamilyRole));
        Assert.All(all, a => Assert.Equal("volume.mute", a.Family!.Id));
        Assert.Equal(new string?[] { null, "volume.master.muted", "volume.master.muted" }, all.Select(a => a.StateId));
        Assert.Equal(OnOffFamily.SiblingIds("volume.mute"), new[] { "volume.mute.on", "volume.mute.off" });
        all[1].Run();
        all[2].Run();
        Assert.Equal(new[] { true, false }, ran);
    }

    [Fact]
    public void OnOffFamily_OnWhenAlreadyOn_IsSkippedThroughTheRegistry()
    {
        var family = new OnOffFamily("t.keep", "Keep awake", "t.state");
        var toggle = new HotkeyAction("t.keep", "Toggle keep awake", "Power", "", _ => { });
        var on = family.Siblings(toggle, _ => { }).First();
        var (registry, said, _) = Setup(new FakeStates { Value = StateValue.Of(true) });
        using var _ = registry;

        Assert.True(registry.SkipsOrWaitsForState(on, null, _ => { }, false));
        Assert.Contains("already on", said.Single());
    }
}
