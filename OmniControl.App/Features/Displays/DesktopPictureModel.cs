using OmniControl.App.Features.Displays.Pages;

namespace OmniControl.App.Features.Displays;

/// The software picture that belongs to the whole desktop rather than one display: saturation, grayscale (saturation 0 as a switch that
/// remembers the saturation before it) and invert, which are one Windows colour effect. Used on the UI thread.
internal sealed class DesktopPictureModel
{
    public const string GrayscaleLabel = "Grayscale (all displays)", InvertLabel = "Invert colours (all displays)";

    /// The names of its cards, in card order.
    public static readonly IReadOnlyList<string> Labels = new[] { DisplayControls.SoftwareSaturation.Label, GrayscaleLabel, InvertLabel };

    private readonly SoftwareDisplayService _service;
    private readonly Action<string> _report;
    private readonly Action _applied;
    private int _saturationBeforeGrayscale = ColorMatrix.SaturationNeutral;

    public int Saturation { get; private set; } = DisplayControls.SoftwareSaturation.Default;
    public bool Invert { get; private set; }
    public bool Grayscale => Saturation == 0;

    /// A value changed.
    public event Action? Changed;

    /// <param name="applied">Told after the colour effect was applied (the preset drop-down re-reads the picture).</param>
    public DesktopPictureModel(SoftwareDisplayService service, Action<string> report, Action applied) =>
        (_service, _report, _applied) = (service, report, applied);

    /// Sets the saturation (the card, a step) and applies the colour effect.
    public OperationResult SetSaturation(int value)
    {
        var spec = DisplayControls.SoftwareSaturation;
        Saturation = Math.Clamp(value, spec.Min, spec.Max);
        return Apply();
    }

    public void SetInvert(bool on)
    {
        Invert = on;
        ApplyReported();
    }

    /// Grayscale on keeps the saturation it replaces; off brings it back.
    public void SetGrayscale(bool on)
    {
        if (on == Grayscale) return;
        if (on) _saturationBeforeGrayscale = Saturation > 0 ? Saturation : ColorMatrix.SaturationNeutral;
        ReportFailure(SetSaturation(on ? 0 : _saturationBeforeGrayscale));
    }

    /// Steps the saturation and returns it as the share of its range the pop-up draws.
    public int Step(int delta)
    {
        var spec = DisplayControls.SoftwareSaturation;
        var now = Math.Clamp(Saturation + delta, spec.Min, spec.Max);
        if (now != Saturation) ReportFailure(SetSaturation(now));
        return spec.OsdPercent(Saturation);
    }

    /// Takes a saved colour effect (a preset) and applies it.
    public void Show(int saturation, bool invert)
    {
        Saturation = Math.Clamp(saturation, DisplayControls.SoftwareSaturation.Min, DisplayControls.SoftwareSaturation.Max);
        Invert = invert;
        ApplyReported();
    }

    /// Back to normal; the page resets the service itself.
    public void Reset()
    {
        (Saturation, Invert, _saturationBeforeGrayscale) = (DisplayControls.SoftwareSaturation.Default, false, ColorMatrix.SaturationNeutral);
        Changed?.Invoke();
    }

    private OperationResult Apply()
    {
        var result = _service.SetColorEffect(Saturation, Invert);
        Changed?.Invoke();
        _applied();
        return result;
    }

    private void ApplyReported() => ReportFailure(Apply());

    private void ReportFailure(OperationResult result)
    {
        if (!result.Success) _report(result.Message);
    }
}
