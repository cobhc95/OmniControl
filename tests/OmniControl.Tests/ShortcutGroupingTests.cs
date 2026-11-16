using OmniControl.App.Hotkeys;
using OmniControl.App.Pages.Shortcuts;
using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// <summary>The grouping data on the master entries: every monitor feature has a section, and folding actions into panels loses none.</summary>
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
    public void ASubgroup_ChangesTheHeading_NotThePanel()
    {
        var plain = Action("a", null);
        var sub = Action("b", "Presets");
        Assert.Equal("Displays - Hardware", plain.Panel);
        Assert.Equal("Displays - Hardware", sub.Panel);
        Assert.Equal("Displays - Hardware - Presets", sub.Heading);
        Assert.Equal("Displays - Hardware", plain.Heading);
    }

    [Fact]
    public void FoldingIntoCards_KeepsEveryAction_InThreeLevels()
    {
        var all = new[] { Action("a", null), Action("b", "Colour"), Action("c", "Colour"), Action("d", "Presets"),
            new HotkeyAction("e", "e", "Displays", "", _ => { }), new HotkeyAction("f", "f", "Displays", "", _ => { }, group: "Software") };
        var group = new HotkeyGroup("Displays", all, _ => { });

        Assert.Equal(new[] { "e" }, group.Actions.Select(a => a.Id));
        Assert.Equal(new[] { "Hardware", "Software" }, group.Subgroups.Select(s => s.Name));
        var hardware = group.Subgroups[0];
        Assert.Equal(new[] { "a" }, hardware.Actions.Select(a => a.Id));
        Assert.Equal(new[] { "Colour", "Presets" }, hardware.Parts.Select(p => p.Name));
        Assert.Equal(new[] { "b", "c" }, hardware.Parts[0].Actions.Select(a => a.Id));
        Assert.Equal("6 shortcuts", group.Summary);
        Assert.Equal("4 shortcuts", hardware.Summary);
    }
}
