using OmniControl.Core.Engine;
using OmniControl.Core.Interfaces;
using Xunit;

namespace OmniControl.Tests;

/// <summary>The orientation mapping between OmniControl's choices and Windows' DEVMODE values (DMDO_*).</summary>
public class DisplayRotationTests
{
    [Theory]
    [InlineData(DisplayOrientation.Landscape, 0u)]
    [InlineData(DisplayOrientation.Portrait, 3u)]          // Settings' "Portrait" is DMDO_270
    [InlineData(DisplayOrientation.LandscapeFlipped, 2u)]
    [InlineData(DisplayOrientation.PortraitFlipped, 1u)]   // "Portrait (flipped)" is DMDO_90
    public void MapsToWindowsValues(DisplayOrientation orientation, uint dmdo)
    {
        Assert.Equal(dmdo, DisplayRotation.ToDmdo(orientation));
        Assert.Equal(orientation, DisplayRotation.FromDmdo(dmdo));
    }

    [Fact]
    public void EveryOrientationRoundTrips()
    {
        foreach (var o in Enum.GetValues<DisplayOrientation>())
            Assert.Equal(o, DisplayRotation.FromDmdo(DisplayRotation.ToDmdo(o)));
    }

    [Fact]
    public void UnknownValueIsNull() => Assert.Null(DisplayRotation.FromDmdo(7));

    [Theory]
    [InlineData(0u, 3u, true)]
    [InlineData(0u, 1u, true)]
    [InlineData(0u, 2u, false)]
    [InlineData(3u, 1u, false)]
    [InlineData(1u, 2u, true)]
    [InlineData(3u, 0u, true)]
    public void SidesSwapOnlyBetweenLandscapeAndPortrait(uint from, uint to, bool swaps) =>
        Assert.Equal(swaps, DisplayRotation.SwapsSides(from, to));
}
