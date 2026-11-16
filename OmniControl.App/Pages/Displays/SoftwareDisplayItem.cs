using OmniControl.App.Display;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One display's picture changes made in software (a gamma ramp): digital brightness, contrast, colour temperature, gamma, channel
/// gains and black and white level (<see cref="DisplayControls.Software"/>). They work on any display, with or without DDC/CI. The
/// values start neutral on every run and are put back when the program ends (<see cref="SoftwareDisplayService"/>). When the display's
/// driver takes no ramps from programs its cards are greyed out. Used on the UI thread.
/// </summary>
internal sealed class SoftwareDisplayItem
{
    private readonly SoftwareDisplayService _service;
    private readonly Action _changed;
    private readonly Dictionary<SoftwareLevel, SliderItem> _sliders = new();

    /// <summary>Windows' name for the display ("\.\DISPLAY1"): what the ramp is set on and the id of its hotkey device.</summary>
    public string Id { get; }
    public string Name { get; }
    public ItemGroup Group { get; }
    public IReadOnlyList<IEditorItem> Cards { get; }

    /// <param name="changed">Told after the picture was applied (the preset drop-down re-reads it).</param>
    public SoftwareDisplayItem(DisplayInfo display, SoftwareDisplayService service, Action<string> report, Action changed)
    {
        (Id, Name, _service, _changed) = (display.DeviceName, display.Name, service, changed);

        foreach (var level in DisplayControls.Software)
        {
            var spec = level.Spec;
            _sliders[level] = new SliderItem(spec.Label, spec.Default, _ => Apply(), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
        }
        Cards = DisplayControls.Software.Select(level => (IEditorItem)_sliders[level]).ToList();
        Group = new ItemGroup($"Displays/Software/{Id}", Name, Cards.Cast<object>().ToList())
        {
            Summary = string.Empty,
            Restore = RestoreDefaults,
            RestoreTip = $"Puts {Name}'s software picture back to normal",
        };
        service.Track(Id);
        ShowRefusal();
    }

    /// <summary>The slider positions now (what a preset keeps).</summary>
    public PictureValues Values
    {
        get
        {
            var values = new PictureValues();
            foreach (var (level, slider) in _sliders) level.Set(values, slider.Value);
            return values;
        }
    }

    /// <summary>The picture the sliders describe, applied.</summary>
    private OperationResult Apply()
    {
        var result = _service.Set(Id, Values.ToColor());
        ShowRefusal();
        _changed();
        return result;
    }

    /// <summary>Greys the cards out, with the reason, when the display's driver takes no ramps from programs; back to normal after a Refresh.</summary>
    public void ShowRefusal()
    {
        foreach (var slider in _sliders.Values)
        {
            if (_service.IsRefused(Id)) slider.Availability.MakeUnavailable(GammaDisplay.DriverRefusal);
            else if (!slider.Availability.IsAvailable) slider.Availability.MakeAvailable();
        }
    }

    /// <summary>Puts the sliders at these positions and applies the picture once (a preset).</summary>
    public void Show(PictureValues values)
    {
        foreach (var (level, slider) in _sliders) slider.Load(Math.Clamp(level.Get(values), level.Spec.Min, level.Spec.Max));
        Apply();
    }

    /// <summary>
    /// Moves the slider of this control by <paramref name="delta"/> (within its range), which applies it, and returns the new value as the
    /// share of its range the pop-up draws.
    /// </summary>
    public int Step(LevelSpec spec, int delta)
    {
        var slider = _sliders.First(s => s.Key.Spec == spec).Value;
        slider.Value = Math.Clamp(slider.Value + delta, spec.Min, spec.Max);
        return spec.OsdPercent(slider.Value);
    }

    /// <summary>Sliders back to neutral and the display's own picture back.</summary>
    public void Reset()
    {
        foreach (var (level, slider) in _sliders) slider.Load(level.Spec.Default);
        _service.Reset(Id);
    }

    private void RestoreDefaults()
    {
        Reset();
        _changed();
    }

    /// <summary>The display is gone: forget its picture without touching it.</summary>
    public void Forget() => _service.Forget(Id);
}
