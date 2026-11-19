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
}
