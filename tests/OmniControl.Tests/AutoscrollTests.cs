using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// <summary>The click-or-drag decision of a held-back button, and what a middle press unused by any action does (<see cref="MiddleClickPolicy"/>).</summary>
public class AutoscrollTests
{
    [Theory]
    [InlineData(0, 0, 20, false)]
    [InlineData(19, -19, 20, false)]
    [InlineData(20, 0, 20, true)]
    [InlineData(0, -25, 20, true)]
    [InlineData(-40, 3, 20, true)]
    public void ClickOrDrag_IsADragFromOneScrollStepOnEitherAxis(int x, int y, int step, bool drag) =>
        Assert.Equal(drag, ClickOrDrag.IsDrag(x, y, step));

    [Theory]
    [InlineData(50000)] // button
    [InlineData(50005)] // hyperlink
    [InlineData(50011)] // menu item
    [InlineData(50019)] // tab item
    public void Decide_PassesOverALinkTabButtonOrMenuItem(int type) =>
        Assert.Equal(ClickVerdict.Pass, MiddleClickPolicy.Decide(false, () => new[] { 50026, type }, 500));

    [Fact]
    public void Decide_SwallowsOverAnythingElse() =>
        Assert.Equal(ClickVerdict.Swallow, MiddleClickPolicy.Decide(false, () => new[] { 50020, 50026, 50033 }, 500));

    [Fact]
    public void Decide_PassesWhileThePointerIsTheHand_WithoutLookingUp()
    {
        var looked = false;
        var verdict = MiddleClickPolicy.Decide(true, () => { looked = true; return Array.Empty<int>(); }, 500);
        Assert.Equal(ClickVerdict.Pass, verdict);
        Assert.False(looked);
    }

    [Fact]
    public void Decide_PassesWhenTheLookupIsTooSlow() =>
        Assert.Equal(ClickVerdict.Pass, MiddleClickPolicy.Decide(false, () => { Thread.Sleep(600); return new[] { 50020 }; }, 30));

    [Fact]
    public void Decide_PassesWhenTheLookupFailsOrSaysNothing()
    {
        Assert.Equal(ClickVerdict.Pass, MiddleClickPolicy.Decide(false, () => null, 500));
        Assert.Equal(ClickVerdict.Pass, MiddleClickPolicy.Decide(false, () => throw new InvalidOperationException(), 500));
    }
}
