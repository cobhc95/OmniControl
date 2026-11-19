using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// The grouping data on the master entries: every monitor feature has a section, and a subgroup extends the heading.
public class ShortcutGroupingTests
{
    [Fact]
    public void EveryMonitorFeature_HasASection_AndItsKeyIsUnchanged()
    {
        Assert.All(VcpFeatures.All, f => Assert.NotEqual(VcpSections.Other, f.Section));
        // These keys are inside stored hotkey and gesture ids (displays.<key>.up), so they never change.
        foreach (var key in new[] { "brightness", "contrast", "red", "green", "blue", "saturation", "speaker", "black.red", "backlight.white", "sixhue.magenta", "input", "preset", "mode", "mute" })
            Assert.Contains(VcpFeatures.All, f => f.Key == key);
        Assert.Equal(VcpFeatures.All.Count, VcpFeatures.All.Select(f => f.Key).Distinct().Count());
    }

    private static HotkeyAction Action(string id, string? subgroup) =>
        new(id, id, "Displays", "", _ => { }, group: "Hardware", subgroup: subgroup);

    [Fact]
    public void ASubgroup_ExtendsTheHeading()
    {
        var plain = Action("a", null);
        var sub = Action("b", "Presets");
        Assert.Equal("Displays - Hardware - Presets", sub.Heading);
        Assert.Equal("Displays - Hardware", plain.Heading);
    }

    [Fact]
    public void AnOldBinding_ReadsAsOneKey_AndKeysStayUnique()
    {
        var old = new StoredHotkey("Ctrl+Alt+K", false, new List<string>()); // a file from before several keys: no More
        Assert.Equal(new[] { "Ctrl+Alt+K" }, old.Keys);

        var all = HotkeyRegistry.Unique(new Dictionary<string, StoredHotkey>
        {
            ["a"] = old,
            ["b"] = new("Ctrl+Alt+K", false, new List<string>(), new List<string> { "Ctrl+Alt+J" }), // a merged backup gave it the same key
        });
        Assert.Equal(new[] { "Ctrl+Alt+K" }, all["a"].Keys);
        Assert.Equal(new[] { "Ctrl+Alt+J" }, all["b"].Keys);
    }

    [Fact]
    public void AnAction_KeepsSeveralKeys_ThroughExportAndRestore()
    {
        var action = Action("a", null);
        action.SetKeys(new[] { "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+1" });
        Assert.Equal(new[] { "Ctrl+Alt+1", "Ctrl+Alt+2" }, action.Keys);
        var copy = Action("b", null);
        copy.Restore(action.Export());
        Assert.Equal(action.Keys, copy.Keys);
    }
}
