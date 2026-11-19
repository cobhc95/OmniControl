using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Displays.Pages;

/// The software controls that belong to the whole desktop rather than one display: saturation, grayscale (saturation 0 as a switch
/// that remembers the saturation before it) and invert, which are one Windows colour effect.
/// Used on the UI thread.
internal sealed class SoftwareGlobalItem
{
    private const string GrayscaleId = "displays.sw.grayscale", InvertId = "displays.sw.invert";

    private readonly SoftwareDisplayService _service;
    private readonly Action<string> _report;
    private readonly Action _changed;
    private readonly SliderItem _saturation;
    private readonly ToggleItem _grayscale, _invert;
    private readonly IOsd _osd;
    private int _saturationBeforeGrayscale = ColorMatrix.SaturationNeutral;

    public IReadOnlyList<IEditorItem> Cards { get; }
    public IReadOnlyList<HotkeyAction> Actions { get; }

    /// The saturation slider's position.
    public int Saturation => _saturation.Value;

    public bool Invert => _invert.IsOn;

    /// <param name="resetAll">Puts every display's picture and the colour effect back (the page owns the displays); the reset hotkey runs it.</param>
    /// <param name="changed">Told after the colour effect was applied (the preset drop-down re-reads the picture).</param>
    public SoftwareGlobalItem(SoftwareDisplayService service, IOsd osd, string category, Action<string> report, Action resetAll, Action changed)
    {
        (_service, _osd, _report, _changed) = (service, osd, report, changed);
        var spec = DisplayControls.SoftwareSaturation;
        _saturation = new SliderItem(spec.Label, spec.Default, _ => Apply(), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
        _grayscale = new ToggleItem("Grayscale (all displays)", false, SetGrayscale)
        {
            Half = true, Tip = "Saturation 0 for every display; turning it off brings back the saturation you had",
        };
        _invert = new ToggleItem("Invert colours (all displays)", false, _ => ApplyReported())
        {
            Half = true, Tip = "Windows' colour effect for every display; the same picture a negative would give",
        };
        Cards = new IEditorItem[] { _saturation, _grayscale, _invert };

        var actions = DisplayActions.Level(spec, DisplayControls.SoftwareKey(spec), category, DisplayControls.SoftwareGroup, (_, delta) => Step(spec, delta), devices: null).ToList();
        actions.Add(DisplayActions.Once(GrayscaleId, "Toggle grayscale", "digital software grayscale black white saturation", category, DisplayControls.SoftwareGroup, VcpSections.Colour,
            _ => _grayscale.IsOn = !_grayscale.IsOn, devices: null));
        actions.Add(DisplayActions.Once(InvertId, "Toggle invert colours", "digital software invert negative colours inverse", category, DisplayControls.SoftwareGroup, VcpSections.Colour,
            _ => _invert.IsOn = !_invert.IsOn, devices: null));
        actions.Add(DisplayActions.Once("displays.sw.reset", "Reset all digital picture changes", "digital software reset normal",
            category, DisplayControls.SoftwareGroup, VcpSections.Presets, _ => resetAll(), devices: null));
        Actions = actions;
    }

    /// Applies the colour effect from the card values; the grayscale switch follows the saturation.
    private OperationResult Apply()
    {
        _grayscale.Load(_saturation.Value == 0);
        var result = _service.SetColorEffect(_saturation.Value, _invert.IsOn);
        _changed();
        return result;
    }

    private void ApplyReported()
    {
        var result = Apply();
        if (!result.Success) _report(result.Message);
    }

    private void SetGrayscale(bool on)
    {
        if (on)
        {
            _saturationBeforeGrayscale = _saturation.Value > 0 ? _saturation.Value : ColorMatrix.SaturationNeutral;
            _saturation.Value = 0;
        }
        else _saturation.Value = _saturationBeforeGrayscale;
    }

    private void Step(LevelSpec spec, int delta)
    {
        _saturation.Value = Math.Clamp(_saturation.Value + delta, spec.Min, spec.Max);
        _osd.Show(spec.Osd, spec.OsdPercent(_saturation.Value));
    }

    /// Shows a saved colour effect (a preset) and applies it.
    public void Show(int saturation, bool invert)
    {
        _saturation.Load(Math.Clamp(saturation, DisplayControls.SoftwareSaturation.Min, DisplayControls.SoftwareSaturation.Max));
        _invert.Load(invert);
        ApplyReported();
    }

    /// The cards back to normal; the page resets the service itself.
    public void Reset()
    {
        _saturation.Load(DisplayControls.SoftwareSaturation.Default);
        _grayscale.Load(false);
        _invert.Load(false);
        _saturationBeforeGrayscale = ColorMatrix.SaturationNeutral;
    }
}
