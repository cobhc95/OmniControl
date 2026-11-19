namespace OmniControl.Core.Engine;

/// Colour saturation and invert for the whole desktop (every display at once, one matrix: <see cref="ColorMatrix"/>) through the Windows Magnification API's full-screen colour
/// effect. Windows removes the effect when the process that set it ends, so a crash cannot leave it behind. Use from one thread
/// (the UI thread): the Magnification runtime belongs to the thread that started it.
public sealed class ColorEffect : IDisposable
{
    private bool _started;

    /// Applies saturation (percent, 100 is normal) and invert to every display; both neutral puts the colours back.
    public OperationResult Set(int saturationPercent, bool invert)
    {
        if (!_started)
        {
            if (ColorMatrix.IsNeutral(saturationPercent, invert)) return OperationResult.Ok(); // nothing to undo, nothing to start
            if (!MagnificationNative.MagInitialize()) return OperationResult.Fail("Windows would not start its colour effect (the Magnification service).");
            _started = true;
        }
        return Apply(ColorMatrix.Build(saturationPercent, invert));
    }

    /// Puts the colours back and releases the Magnification runtime.
    public void Reset()
    {
        if (!_started) return;
        Apply(ColorMatrix.Build(ColorMatrix.SaturationNeutral, invert: false));
        MagnificationNative.MagUninitialize();
        _started = false;
    }

    private static OperationResult Apply(float[] matrix)
    {
        var effect = new MagnificationNative.MAGCOLOREFFECT { transform = matrix };
        return MagnificationNative.MagSetFullscreenColorEffect(ref effect)
            ? OperationResult.Ok()
            : OperationResult.Fail("Windows refused the colour effect.");
    }

    public void Dispose() => Reset();
}
