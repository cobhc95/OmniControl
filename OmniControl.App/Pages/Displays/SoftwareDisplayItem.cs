using OmniControl.App.Display;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One display's picture changes made in software (a gamma ramp): digital brightness, digital contrast and colour temperature. They work on any display, with or without DDC/CI. The values start neutral on every run and are put back when the program ends
/// (<see cref="SoftwareDisplayService"/>). Used on the UI thread.
/// </summary>
internal sealed class SoftwareDisplayItem
{
    private readonly SoftwareDisplayService _service;
    private readonly SliderItem _brightness, _contrast, _warmth;

    /// <summary>Windows' name for the display ("\.\DISPLAY1"): what the ramp is set on and the id of its hotkey device.</summary>
    public string Id { get; }
    public string Name { get; }
    public ItemGroup Group { get; }
    public IReadOnlyList<IEditorItem> Cards { get; }

    public SoftwareDisplayItem(DisplayInfo display, SoftwareDisplayService service, Action<string> report)
    {
        (Id, Name, _service) = (display.DeviceName, display.Name, service);

        _brightness = Slider(DisplayControls.SoftwareBrightness, report);
        _contrast = Slider(DisplayControls.SoftwareContrast, report);
        _warmth = Slider(DisplayControls.SoftwareWarmth, report);

        Cards = new IEditorItem[] { _brightness, _contrast, _warmth };
        Group = new ItemGroup($"Displays/Software/{Id}", Name, Cards.Cast<object>().ToList());
    }

    private SliderItem Slider(LevelSpec spec, Action<string> report) =>
        new(spec.Label, spec.Default, _ => Apply(), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };

    /// <summary>The picture the three sliders describe.</summary>
    private OperationResult Apply() =>
        _service.Set(Id, new SoftwareColor(_brightness.Value, _contrast.Value, SoftwareColor.KelvinOfWarmth(_warmth.Value)));

    /// <summary>
    /// Moves the slider of this control by <paramref name="delta"/> (within its range), which applies it, and returns the new value as the
    /// share of its range the pop-up draws.
    /// </summary>
    public int Step(LevelSpec spec, int delta)
    {
        var slider = spec == DisplayControls.SoftwareBrightness ? _brightness : spec == DisplayControls.SoftwareContrast ? _contrast : _warmth;
        slider.Value = Math.Clamp(slider.Value + delta, spec.Min, spec.Max);
        return spec.OsdPercent(slider.Value);
    }

    /// <summary>Sliders back to neutral and the display's own picture back.</summary>
    public void Reset()
    {
        _brightness.Load(DisplayControls.SoftwareBrightness.Default);
        _contrast.Load(DisplayControls.SoftwareContrast.Default);
        _warmth.Load(DisplayControls.SoftwareWarmth.Default);
        _service.Reset(Id);
    }

    /// <summary>The display is gone: forget its picture without touching it.</summary>
    public void Forget() => _service.Forget(Id);
}
