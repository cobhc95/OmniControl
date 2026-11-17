namespace OmniControl.Core.Engine;

/// <summary>
/// Tells whether a screenshot can be trusted to measure colours and shapes. A hidden emulator without a graphics surface answers with nothing,
/// one flat colour (black) or a picture of the wrong size; then the state is read from the screen dump alone.
/// </summary>
public static class ScreenshotCheck
{
    /// <summary>True when a sample of the picture is one colour (every channel within <see cref="CoreTiming.ScreenBlankTolerance"/> of the first pixel).</summary>
    public static bool IsBlank(RgbaImage image)
    {
        if (image.Width == 0 || image.Height == 0) return true;
        var first = image.At(0, 0);
        for (var row = 0; row < CoreTiming.ScreenBlankGrid; row++)
        {
            for (var column = 0; column < CoreTiming.ScreenBlankGrid; column++)
            {
                var (r, g, b) = image.At(column * (image.Width - 1) / (CoreTiming.ScreenBlankGrid - 1), row * (image.Height - 1) / (CoreTiming.ScreenBlankGrid - 1));
                if (Math.Abs(r - first.R) > CoreTiming.ScreenBlankTolerance || Math.Abs(g - first.G) > CoreTiming.ScreenBlankTolerance
                    || Math.Abs(b - first.B) > CoreTiming.ScreenBlankTolerance) return false;
            }
        }
        return true;
    }

    /// <summary>One plain phrase saying why the picture cannot be used; null when it can. The screen's size comes from the dump (0 = unknown).</summary>
    public static string? Problem(RgbaImage? image, int screenWidth, int screenHeight)
    {
        if (image is null) return "adb gave no readable screenshot";
        if (IsBlank(image)) return "the screenshot is one flat colour (the emulator's screen is probably black)";
        if (screenWidth > 0 && screenHeight > 0
            && Math.Abs((double)image.Width * screenHeight - (double)image.Height * screenWidth) > CoreTiming.ScreenShapeTolerance * image.Height * screenWidth)
            return $"the screenshot is {image.Width}x{image.Height} but the screen is {screenWidth}x{screenHeight}";
        return null;
    }
}
