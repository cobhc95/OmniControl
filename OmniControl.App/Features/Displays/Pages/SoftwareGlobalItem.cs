namespace OmniControl.App.Features.Displays.Pages;

/// The whole desktop's software cards (saturation, grayscale, invert): a view of the <see cref="DesktopPictureModel"/>, made with the page.
internal sealed class SoftwareGlobalItem : IDisposable
{
    private readonly DesktopPictureModel _model;
    private readonly SliderItem _saturation;
    private readonly ToggleItem _grayscale, _invert;

    public IReadOnlyList<IEditorItem> Cards { get; }

    public SoftwareGlobalItem(DesktopPictureModel model, Action<string> report)
    {
        _model = model;
        var spec = DisplayControls.SoftwareSaturation;
        _saturation = new SliderItem(DisplayCards.Saturation, model.Saturation, model.SetSaturation, report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
        _grayscale = new ToggleItem(DisplayCards.Grayscale, model.Grayscale, model.SetGrayscale) { Half = true };
        _invert = new ToggleItem(DisplayCards.Invert, model.Invert, model.SetInvert) { Half = true };
        Cards = new IEditorItem[] { _saturation, _grayscale, _invert };
        model.Changed += Show;
    }

    private void Show()
    {
        CardMirror.Show(_saturation, _model.Saturation, null);
        _grayscale.Load(_model.Grayscale);
        _invert.Load(_model.Invert);
    }

    public void Dispose() => _model.Changed -= Show;
}
