using OmniControl.App.Shared.Presets;

namespace OmniControl.Tests;

/// A preset's "Switch to" with "Run again to switch back", and a group's Next and Previous.
public class PresetSteppingTests
{
    private static readonly string[] Order = { "a", "b", "c" };

    [Fact]
    public void SwitchBackOff_AlwaysAppliesThePreset() => Assert.Equal("b", PresetStepping.Target("b", inUse: "b", before: "a", switchBack: false));

    [Fact]
    public void SwitchBackOn_WhileInUse_GoesBackToTheOneBefore() => Assert.Equal("a", PresetStepping.Target("b", inUse: "b", before: "a", switchBack: true));

    [Fact]
    public void SwitchBackOn_WhileAnotherIsInUse_SwitchesToThePreset() => Assert.Equal("b", PresetStepping.Target("b", inUse: "c", before: "a", switchBack: true));

    [Fact]
    public void SwitchBackOn_WithNothingBefore_AppliesThePreset() => Assert.Equal("b", PresetStepping.Target("b", inUse: "b", before: null, switchBack: true));

    [Theory]
    [InlineData("a", 1, "b")]
    [InlineData("c", 1, "a")]
    [InlineData("a", -1, "c")]
    [InlineData("b", -1, "a")]
    [InlineData(null, 1, "a")]
    [InlineData(null, -1, "c")]
    [InlineData("gone", 1, "a")]
    public void NextAndPrevious_StepInOrder_AndWrap(string? inUse, int direction, string expected) =>
        Assert.Equal(expected, PresetStepping.Step(Order, inUse, direction));

    [Fact]
    public void AnEmptyGroup_HasNoNextPreset() => Assert.Null(PresetStepping.Step(Array.Empty<string>(), null, 1));
}
