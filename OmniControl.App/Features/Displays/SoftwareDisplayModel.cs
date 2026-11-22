using OmniControl.App.Features.Displays.Pages;

namespace OmniControl.App.Features.Displays;

/// One display's picture changes made in software (a gamma ramp): digital brightness, contrast, colour temperature, gamma, channel
/// gains and black and white level (<see cref="DisplayControls.Software"/>). They work on any display, with or without DDC/CI. The
/// values start neutral on every run and are put back when the program ends (<see cref="SoftwareDisplayService"/>). When the display's
/// driver takes no ramps from programs the controls cannot be used (<see cref="Reason"/>). Used on the UI thread.
internal sealed class SoftwareDisplayModel
{
    private readonly SoftwareDisplayService _service;
    private readonly Action _applied;
    private readonly Dictionary<SoftwareLevel, int> _values = DisplayControls.Software.ToDictionary(l => l, l => l.Spec.Default);

    /// Windows' name for the display ("\.\DISPLAY1"): what the ramp is set on and the id of its hotkey device.
    public string Id { get; }
    public string Name { get; }

    /// Why the controls cannot be used now (the driver refuses ramps); null: they can.
    public string? Reason { get; private set; }

    /// A value or <see cref="Reason"/> changed.
    public event Action? Changed;

    /// <param name="applied">Told after the picture was applied (the preset drop-down re-reads it).</param>
    public SoftwareDisplayModel(DisplayInfo display, SoftwareDisplayService service, Action applied)
    {
        (Id, Name, _service, _applied) = (display.DeviceName, display.Name, service, applied);
        service.Track(Id);
        Reason = Refusal;
    }

    public int this[SoftwareLevel level] => _values[level];

    /// The values now (what a preset keeps).
    public PictureValues Values
    {
        get
        {
            var values = new PictureValues();
            foreach (var (level, value) in _values) level.Set(values, value);
            return values;
        }
    }

    /// Sets one control (a card) and applies the picture.
    public OperationResult Set(SoftwareLevel level, int value)
    {
        _values[level] = Math.Clamp(value, level.Spec.Min, level.Spec.Max);
        return Apply();
    }

    private OperationResult Apply()
    {
        var result = _service.Set(Id, Values.ToColor());
        Reason = Refusal;
        Changed?.Invoke();
        _applied();
        return result;
    }

    private string? Refusal => _service.IsRefused(Id) ? GammaDisplay.DriverRefusal : null;

    /// The driver may take ramps again after a Refresh, or may have refused one the schedule set: <see cref="Reason"/> follows.
    public void ShowRefusal()
    {
        if (Reason == Refusal) return;
        Reason = Refusal;
        Changed?.Invoke();
    }

    /// Takes these values and applies the picture once (a preset).
    public void Show(PictureValues values)
    {
        foreach (var level in DisplayControls.Software) _values[level] = Math.Clamp(level.Get(values), level.Spec.Min, level.Spec.Max);
        Apply();
    }

    /// Moves this control by <paramref name="delta"/> (within its range), which applies it, and returns the new value as the share of its range the pop-up draws.
    public int Step(LevelSpec spec, int delta)
    {
        var level = DisplayControls.Software.First(l => l.Spec == spec);
        var now = Math.Clamp(_values[level] + delta, spec.Min, spec.Max);
        if (now != _values[level]) Set(level, now);
        return spec.OsdPercent(_values[level]);
    }

    /// Values back to neutral and the display's own picture back.
    public void Reset()
    {
        foreach (var level in DisplayControls.Software) _values[level] = level.Spec.Default;
        _service.Reset(Id);
        Changed?.Invoke();
    }

    /// Back to neutral on its own (the group's Restore defaults).
    public void RestoreDefaults()
    {
        Reset();
        _applied();
    }

    /// The display is gone: forget its picture without touching it.
    public void Forget() => _service.Forget(Id);
}
