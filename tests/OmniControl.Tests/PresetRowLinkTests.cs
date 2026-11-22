using OmniControl.App.Shared.Presets;
using OmniControl.App.Shared.Presets.Manager;
using Xunit;

namespace OmniControl.Tests;

/// A preset row's link to its rule in Automation: its words with and without a rule, and that a click reaches the card.
public class PresetRowLinkTests
{
    private sealed class Owner : IPresetRowOwner
    {
        public PresetRowViewModel? Edited;
        public void Apply(PresetRowViewModel row) { }
        public void Rename(PresetRowViewModel row, string name) { }
        public void RenameStarted(PresetRowViewModel row) { }
        public void Delete(PresetRowViewModel row) { }
        public void Duplicate(PresetRowViewModel row) { }
        public void Export(PresetRowViewModel row) { }
        public void SetShortcut(PresetRowViewModel row, string gesture) { }
        public void EditRule(PresetRowViewModel row) => Edited = row;
    }

    private static PresetRowViewModel Row(PresetRule? rule, bool hasRules, Owner owner) =>
        new(new PresetEntry("p1", "Gaming"), string.Empty, rule, hasRules, "Switch to", false, false, owner);

    [Fact]
    public void Link_tells_of_the_rule_or_offers_one_and_reaches_the_card()
    {
        var owner = new Owner();
        var with = Row(new PresetRule("When Steam running"), true, owner);
        Assert.Equal("* When Steam running · Edit in Automation", with.RuleLink);
        Assert.Equal("More options in Automation", Row(null, true, owner).RuleLink);
        Assert.False(Row(null, false, owner).HasRuleLink);
        with.EditRuleCommand.Execute(null);
        Assert.Same(with, owner.Edited);
    }
}
