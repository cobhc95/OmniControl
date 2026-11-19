namespace OmniControl.Core.Engine;

/// The colour matrix of the whole-desktop effect (pure, no Windows calls): saturation, then invert. Grayscale is saturation 0. A ramp
/// cannot do these (it treats each colour channel alone), so they are one matrix for the Magnification API, not part of a display's ramp.
/// The 5x5 matrix is row-major and multiplies the row [red green blue alpha 1].
public static class ColorMatrix
{
    public const int SaturationMin = 0, SaturationMax = 150, SaturationNeutral = 100;

    // Luminance weights of red, green and blue (Rec. 601); greyscale keeps the brightness of each colour.
    private static readonly float[] Luminance = { 0.3086f, 0.6094f, 0.0820f };

    public static bool IsNeutral(int saturationPercent, bool invert) => saturationPercent == SaturationNeutral && !invert;

    public static float[] Build(int saturationPercent, bool invert)
    {
        var s = Math.Clamp(saturationPercent, SaturationMin, SaturationMax) / 100f;
        var matrix = new float[25];
        // Row = the input colour, column = the output colour; the fourth (alpha) and fifth (offset) rows and columns stay as in the identity.
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                matrix[row * 5 + column] = Luminance[row] * (1 - s) + (row == column ? s : 0);
        matrix[3 * 5 + 3] = 1;
        matrix[4 * 5 + 4] = 1;

        if (invert)
        {
            // 1 - colour: the colour part negated, the offset row brings it back up.
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    matrix[row * 5 + column] = -matrix[row * 5 + column];
            for (var column = 0; column < 3; column++) matrix[4 * 5 + column] = 1;
        }
        return matrix;
    }
}
