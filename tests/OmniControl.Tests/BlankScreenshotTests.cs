using OmniControl.Core;
using OmniControl.Core.Engine;
using Xunit;

namespace OmniControl.Tests;

/// <summary>A hidden emulator may answer with no screenshot, a black one or one of the wrong size: that is detected, and the state is then read from the screen dump alone.</summary>
public sealed class BlankScreenshotTests
{
    private static RgbaImage Flat(int width, int height, byte value, Action<byte[], int>? paint = null)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (value, value, value, 255);
        paint?.Invoke(pixels, width);
        return new RgbaImage(width, height, pixels);
    }

    [Fact]
    public void A_black_or_one_colour_screenshot_is_blank_and_a_picture_with_content_is_not()
    {
        Assert.True(ScreenshotCheck.IsBlank(Flat(540, 960, 0)));
        Assert.True(ScreenshotCheck.IsBlank(Flat(540, 960, 3))); // near black noise
        var content = Flat(540, 960, 0, (pixels, width) =>
        {
            for (var y = 0; y < 960; y++)
                for (var x = 0; x < width / 2; x++) pixels[(y * width + x) * 4] = 200;
        });
        Assert.False(ScreenshotCheck.IsBlank(content));
    }

    [Fact]
    public void A_missing_blank_or_wrongly_shaped_screenshot_is_named_plainly()
    {
        var content = Flat(540, 960, 0, (pixels, width) => { for (var x = 0; x < width; x++) pixels[x * 4] = 255; pixels[(480 * width + 5) * 4 + 1] = 255; });
        Assert.Contains("no readable screenshot", ScreenshotCheck.Problem(null, 1080, 1920));
        Assert.Contains("one flat colour", ScreenshotCheck.Problem(Flat(540, 960, 0), 1080, 1920));
        Assert.Contains("but the screen is 1080x1080", ScreenshotCheck.Problem(content, 1080, 1080));
        Assert.Null(ScreenshotCheck.Problem(content, 1080, 1920));
        Assert.Null(ScreenshotCheck.Problem(content, 0, 0)); // a screen of unknown size is not judged
    }

    [Fact]
    public void Without_a_usable_picture_the_state_is_read_from_the_dump_signals_alone()
    {
        var model = new StateModel();
        model.Signals.Add(new StateSignal { Key = "attr:Switch#x@0.checked", OnFact = "true", OffFact = "false", Weight = 1 });
        model.Signals.Add(new StateSignal { Key = "colour:target", On = new[] { 0.0, 1.0 }, Off = new[] { 0.0, 0.0 }, Weight = 1 });
        var blank = new StateSample { PixelsMissing = true };
        blank.Facts["attr:Switch#x@0.checked"] = "true";
        var off = new StateSample { PixelsMissing = true };
        off.Facts["attr:Switch#x@0.checked"] = "false";

        var on = model.Read(blank);
        Assert.Equal(DeviceState.On, on.State);
        Assert.Equal(1, on.Confidence, 3); // the colour family, which has nothing to measure, does not dilute it
        Assert.Equal(DeviceState.Off, model.Read(off).State);
    }
}
