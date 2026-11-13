using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// Colour saturation for the whole desktop (every display at once) through the Windows Magnification API's full-screen colour
/// effect. Windows removes the effect when the process that set it ends, so a crash cannot leave it behind. Use from one thread
/// (the UI thread): the Magnification runtime belongs to the thread that started it.
/// </summary>
public sealed class ColorEffect : IDisposable
{
    public const int SaturationMin = 0, SaturationMax = 150, SaturationNeutral = 100;

    // Luminance weights of red, green and blue (Rec. 601); greyscale keeps the brightness of each colour.
    private static readonly float[] Luminance = { 0.3086f, 0.6094f, 0.0820f };

    private bool _started;

    public OperationResult SetSaturation(int percent)
    {
        percent = Math.Clamp(percent, SaturationMin, SaturationMax);
        if (!_started)
        {
            if (percent == SaturationNeutral) return OperationResult.Ok(); // nothing to undo, nothing to start
            if (!MagnificationNative.MagInitialize()) return OperationResult.Fail("Windows would not start its colour effect (the Magnification service).");
            _started = true;
        }
        return Apply(percent);
    }

    /// <summary>Puts the colours back and releases the Magnification runtime.</summary>
    public void Reset()
    {
        if (!_started) return;
        Apply(SaturationNeutral);
        MagnificationNative.MagUninitialize();
        _started = false;
    }

    private static OperationResult Apply(int percent)
    {
        var s = percent / 100f;
        var matrix = new float[25];
        // Row = the input colour, column = the output colour; the fourth (alpha) and fifth (offset) rows and columns stay as in the identity.
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                matrix[row * 5 + column] = Luminance[row] * (1 - s) + (row == column ? s : 0);
        matrix[3 * 5 + 3] = 1;
        matrix[4 * 5 + 4] = 1;

        var effect = new MagnificationNative.MAGCOLOREFFECT { transform = matrix };
        return MagnificationNative.MagSetFullscreenColorEffect(ref effect)
            ? OperationResult.Ok()
            : OperationResult.Fail("Windows refused the colour effect.");
    }

    public void Dispose() => Reset();
}
