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
    public void FoldingIntoPanels_KeepsEveryAction()
    {
        var all = new[] { Action("a", null), Action("b", "Colour"), Action("c", "Colour"), Action("d", "Presets") };
        var group = new HotkeyGroup("Displays - Hardware", all);

        var kept = group.Actions.Concat(group.Subgroups.SelectMany(s => s.Actions)).Select(a => a.Id).OrderBy(i => i);
        Assert.Equal(new[] { "a", "b", "c", "d" }, kept);
        Assert.Equal(new[] { "Colour", "Presets" }, group.Subgroups.Select(s => s.Name));
    }
}
