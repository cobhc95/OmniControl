using OmniControl.Core.Engine;
using OmniControl.App.Display;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The software controls that belong to the whole desktop rather than one display: saturation, grayscale (saturation 0 as a switch
/// that remembers the saturation before it) and invert, which are one Windows colour effect, and one Reset for every software change.
/// Used on the UI thread.
/// </summary>
internal sealed class SoftwareGlobalItem
{
    private const string GrayscaleId = "displays.sw.grayscale", InvertId = "displays.sw.invert";

    private readonly SoftwareDisplayService _service;
    private readonly Action<string> _report;
    private readonly SliderItem _saturation;
    private readonly ToggleItem _grayscale, _invert;
    private readonly OsdService _osd;
    private int _saturationBeforeGrayscale = ColorMatrix.SaturationNeutral;

    public IReadOnlyList<IEditorItem> Cards { get; }
    public IReadOnlyList<HotkeyAction> Actions { get; }

    /// <summary>The saturation slider's position.</summary>
    public int Saturation => _saturation.Value;

    public bool Invert => _invert.IsOn;

    /// <param name="resetAll">Puts every display's picture and the colour effect back (the page owns the displays).</param>
    public SoftwareGlobalItem(SoftwareDisplayService service, OsdService osd, string category, Action<string> report, Action resetAll)
    {
        (_service, _osd, _report) = (service, osd, report);
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
        var reset = new ButtonItem("Reset everything", "Reset all", "All picture and colour settings back to normal", resetAll) { Half = true };
        Cards = new IEditorItem[] { _saturation, _grayscale, _invert, reset };

        var actions = DisplayActions.Level(spec, DisplayControls.SoftwareKey(spec), category, DisplayControls.SoftwareGroup, (_, delta) => Step(spec, delta), devices: null).ToList();
        actions.Add(DisplayActions.Once(GrayscaleId, "Toggle grayscale", "digital software grayscale black white saturation", category, DisplayControls.SoftwareGroup, VcpSections.Colour,
            _ => _grayscale.IsOn = !_grayscale.IsOn, devices: null));
        actions.Add(DisplayActions.Once(InvertId, "Toggle invert colours", "digital software invert negative colours inverse", category, DisplayControls.SoftwareGroup, VcpSections.Colour,
            _ => _invert.IsOn = !_invert.IsOn, devices: null));
        actions.Add(DisplayActions.Once("displays.sw.reset", "Reset all digital picture changes", "digital software reset normal",
            category, DisplayControls.SoftwareGroup, VcpSections.Presets, _ => resetAll(), devices: null));
        Actions = actions;
    }

    /// <summary>Applies the colour effect from the card values; the grayscale switch follows the saturation.</summary>
    private OperationResult Apply()
    {
        _grayscale.Load(_saturation.Value == 0);
        return _service.SetColorEffect(_saturation.Value, _invert.IsOn);
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

    /// <summary>Shows a saved colour effect (a preset) and applies it.</summary>
    public void Show(int saturation, bool invert)
    {
        _saturation.Load(Math.Clamp(saturation, DisplayControls.SoftwareSaturation.Min, DisplayControls.SoftwareSaturation.Max));
        _invert.Load(invert);
        ApplyReported();
    }

    /// <summary>The cards back to normal; the page resets the service itself.</summary>
    public void Reset()
    {
        _saturation.Load(DisplayControls.SoftwareSaturation.Default);
        _grayscale.Load(false);
        _invert.Load(false);
        _saturationBeforeGrayscale = ColorMatrix.SaturationNeutral;
    }
}
