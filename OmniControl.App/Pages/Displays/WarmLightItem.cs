using OmniControl.App.Display;
using OmniControl.App.Hotkeys;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The warm-light schedule's cards: one line that says honestly what it is doing ("Active until 07:00", "Starts at 21:00", "Off") with
/// its on/off button, the two times and the temperature it warms to. The schedule layers over each display's own colour temperature
/// slider (<see cref="WarmLightScheduler"/>): it warms towards the target and never cools. Used on the UI thread.
/// </summary>
internal sealed class WarmLightItem
{
    private const string TurnOn = "Turn on", TurnOff = "Turn off";
    private const string WarmId = "displays.sw.warmlight";

    private readonly WarmLightScheduler _scheduler;
    private readonly DisplaySettingsStore _store;
    private readonly ButtonItem _state;
    private readonly ChoiceItem _start, _end;
    private readonly SliderItem _target;
    private readonly IReadOnlyList<string> _timeNames;

    public IReadOnlyList<IEditorItem> Cards { get; }
    public IReadOnlyList<HotkeyAction> Actions { get; }

    public WarmLightItem(WarmLightScheduler scheduler, DisplaySettingsStore store, string category, Action<string> report)
    {
        (_scheduler, _store) = (scheduler, store);
        var settings = store.WarmLight;
        var times = WarmSchedule.Times().ToList();
        var names = _timeNames = times.Select(WarmSchedule.Clock).ToList();

        _state = new ButtonItem("Warm light at night", settings.Enabled ? TurnOff : TurnOn, scheduler.StateText, Toggle);
        _start = new ChoiceItem("Warm from", names, WarmSchedule.Clock(Nearest(times, settings.Start)), name => ChooseTime(name, t => settings.Start = t)) { Half = true };
        _end = new ChoiceItem("Warm until", names, WarmSchedule.Clock(Nearest(times, settings.End)), name => ChooseTime(name, t => settings.End = t)) { Half = true };
        var kelvin = Math.Clamp(settings.Kelvin, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral);
        _target = new SliderItem("Warm light temperature", kelvin, SetTarget, report, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral,
            " K", new WarmLightSettings().Kelvin) { Half = true };
        Cards = new IEditorItem[] { _state, _start, _end, _target };

        Actions = new[]
        {
            DisplayActions.Once(WarmId, "Toggle warm light at night", "warm light night schedule evening blue light filter", category, DisplayControls.SoftwareGroup, VcpSections.Colour,
                _ => Toggle(), devices: null, icon: "MoonIcon"),
        };
        scheduler.Changed += Refresh;
    }

    /// <summary>Stops listening to the schedule.</summary>
    public void Dispose() => _scheduler.Changed -= Refresh;

    private void Toggle() => SetEnabled(!IsOn);

    /// <summary>The schedule is on (the page's state reads it).</summary>
    public bool IsOn => _store.WarmLight.Enabled;

    /// <summary>Turns the schedule on or off (the button, its action and "Restore defaults").</summary>
    public void SetEnabled(bool on)
    {
        if (_store.WarmLight.Enabled == on) return;
        _store.WarmLight.Enabled = on;
        Changed();
    }

    /// <summary>Off, with the default times and temperature, the cards showing them.</summary>
    public void Reset()
    {
        var defaults = new WarmLightSettings();
        var settings = _store.WarmLight;
        (settings.Enabled, settings.Start, settings.End, settings.Kelvin) = (defaults.Enabled, defaults.Start, defaults.End, defaults.Kelvin);
        var times = WarmSchedule.Times().ToList();
        _start.Load(_timeNames, WarmSchedule.Clock(Nearest(times, defaults.Start)));
        _end.Load(_timeNames, WarmSchedule.Clock(Nearest(times, defaults.End)));
        _target.Load(defaults.Kelvin);
        Changed();
    }

    private void ChooseTime(string name, Action<int> set)
    {
        set(WarmSchedule.Times().First(t => WarmSchedule.Clock(t) == name));
        Changed();
    }

    private OperationResult SetTarget(int kelvin)
    {
        _store.WarmLight.Kelvin = kelvin;
        Changed();
        return OperationResult.Ok();
    }

    private void Changed()
    {
        _store.SaveWarmLight();
        _scheduler.Update();
    }

    private void Refresh()
    {
        _state.Description = _scheduler.StateText;
        _state.ButtonText = _store.WarmLight.Enabled ? TurnOff : TurnOn;
    }

    /// <summary>The offered time closest to a saved one (the lists offer half hours; a hand-edited file may hold other minutes).</summary>
    private static int Nearest(IReadOnlyList<int> times, int minute) => times.MinBy(t => Math.Abs(t - minute));
}
