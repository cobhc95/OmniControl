using OmniControl.App.Features.Resources.Gadget;

namespace OmniControl.Tests;

/// The gadget label parser (<see cref="GadgetLabel.Parts"/>): plain words, {icon}, {icon:colour} and a mix, as every gadget drawing reads them.
public class GadgetLabelTests
{
    [Fact]
    public void PlainText_IsOneWordsPart() =>
        Assert.Equal(new[] { new LabelPart("↑:", null) }, GadgetLabel.Parts("↑:"));

    [Fact]
    public void IconToken_IsAnIconWithoutColour() =>
        Assert.Equal(new[] { new LabelPart(null, "upload") }, GadgetLabel.Parts("{upload}"));

    [Fact]
    public void ColouredToken_IsAnIconWithItsColour()
    {
        var part = Assert.Single(GadgetLabel.Parts(GadgetLabel.Token("upload", GadgetLabel.UploadColour)));
        Assert.Equal(new LabelPart(null, "upload", GadgetLabel.UploadColour), part);
        Assert.NotNull(part.Ink);
    }

    [Fact]
    public void Mixed_KeepsOrder() =>
        Assert.Equal(new[] { new LabelPart(null, "cpu"), new LabelPart(null, "temperature", "orange"), new LabelPart(":", null) },
            GadgetLabel.Parts("{cpu}{temperature:orange}:"));

    [Fact]
    public void UserIconId_KeepsItsColon() =>
        Assert.Equal(new[] { new LabelPart(null, "user:abc") }, GadgetLabel.Parts("{user:abc}"));

    [Fact]
    public void BraceWithoutId_StaysWords() =>
        Assert.Equal(new[] { new LabelPart("{a b}", null) }, GadgetLabel.Parts("{a b}"));

    [Theory]
    [InlineData(6, null)] // before "{cpu}" (it starts at 6)
    [InlineData(7, 6)] // inside it
    [InlineData(11, null)] // just after its closing brace
    public void TokenAt_FindsTheTokenAroundTheCaret(int caret, int? start) =>
        Assert.Equal(start, GadgetLabel.TokenAt("CPU:  {cpu} x", caret)?.Start);

    [Fact]
    public void Recolour_TheSelectedIconOnly() =>
        Assert.Equal("{cpu}{temperature:red}:", GadgetLabel.Recolour("{cpu}{temperature:orange}:", 5, "red"));

    [Fact]
    public void Recolour_EveryIconWhenNoneIsSelected() =>
        Assert.Equal("a{cpu:blue}b{flame:blue}", GadgetLabel.Recolour("a{cpu}b{flame:red}", null, "blue"));

    [Fact]
    public void Recolour_FollowTextWritesThePlainToken() =>
        Assert.Equal("{cpu}{temperature}:", GadgetLabel.Recolour("{cpu}{temperature:orange}:", 5, null));

    [Fact]
    public void Recolour_LeavesWordsAndOldLabelsAlone() =>
        Assert.Equal("CPU {a b}", GadgetLabel.Recolour("CPU {a b}", null, "green"));

    [Fact]
    public void ColourOf_TheSelectedOrTheSharedColour()
    {
        Assert.Equal("orange", GadgetLabel.ColourOf("{cpu}{temperature:orange}", 5));
        Assert.Null(GadgetLabel.ColourOf("{cpu}{temperature:orange}", null)); // mixed
        Assert.Equal("blue", GadgetLabel.ColourOf("{cpu:blue}x{flame:blue}", null));
    }

    [Fact]
    public void IconColours_LeaveOutBlack() => Assert.DoesNotContain("black", GadgetLabel.IconColours);

    [Theory]
    [InlineData("{cpu-2:white}", 7, "{cpu-2:white}{gpu}", 18)] // inside a token: after it, never into it
    [InlineData("{cpu-2:white}", 0, "{gpu}{cpu-2:white}", 5)]
    [InlineData("ab", 1, "a{gpu}b", 6)]
    [InlineData("ab", 9, "ab{gpu}", 7)]
    public void Insert_NeverWritesATokenIntoAnother(string label, int caret, string expected, int newCaret) =>
        Assert.Equal((expected, newCaret), GadgetLabel.Insert(label, caret, "{gpu}"));

    [Fact]
    public void Replace_SwapsOneTokenAndKeepsItsColour()
    {
        Assert.Equal(("x{gpu:white}{cpu}", 11), GadgetLabel.Replace("x{cpu-2:white}{cpu}", 1, "gpu"));
        Assert.Null(GadgetLabel.Replace("x{cpu}", 0, "gpu")); // no token starts there
    }

    [Fact]
    public void Replace_ClickedThroughManyIcons_StaysOneToken()
    {
        var label = "{cpu-2:white}";
        foreach (var id in new[] { "cpu", "device-desktop-analytics", "server", "cpu-2" }) label = GadgetLabel.Replace(label, 0, id)!.Value.Label;
        Assert.Equal("{cpu-2:white}", label);
    }

    [Theory]
    [InlineData("{cpu-2:{device-desktop-analytics:white}white}", "{device-desktop-analytics:white}")]
    [InlineData("{cpu-2:{device-desktop-analytics: white}white}", "{device-desktop-analytics:white}")]
    [InlineData("{a:{b:{cpu}x}y}T:", "{cpu}T:")]
    [InlineData("{cpu}{temperature:orange}:", "{cpu}{temperature:orange}:")]
    [InlineData("CPU {a b} {user:abc}", "CPU {a b} {user:abc}")]
    [InlineData("", "")]
    public void Repaired_MendsNestedTokensOnly(string label, string expected) => Assert.Equal(expected, GadgetLabel.Repaired(label));

    [Fact]
    public void Repaired_EveryIconAndColour_RoundTrips()
    {
        foreach (var colour in GadgetLabel.IconColours.Cast<string?>().Prepend(null))
        foreach (var id in new[] { "cpu", "cpu-2", "device-desktop-analytics", "temperature", "user:abc" })
        {
            var token = GadgetLabel.Token(id, colour);
            Assert.Equal(token, GadgetLabel.Repaired(token));
            Assert.Equal(new LabelPart(null, id, colour), Assert.Single(GadgetLabel.Parts(token)));
        }
    }

    [Fact]
    public void Complete_RepairsASavedNestedLabel()
    {
        var look = new OmniControl.App.Features.Resources.GadgetSettings();
        look.Readings[0].Label = "{cpu-2:{device-desktop-analytics: white}white}";
        look.Complete();
        Assert.Contains(look.Readings, r => r.Label == "{device-desktop-analytics:white}");
    }

    [Fact]
    public void InkOf_AnUnknownColourIsNone() => Assert.Null(GadgetLabel.InkOf("teal"));
}
