namespace OmniControl.App.Features.Displays.Pages;

/// The warm-light schedule's cards, a view of the <see cref="WarmLightModel"/> made with the page: one line that says honestly what it is
/// doing ("Active until 07:00", "Starts at 21:00", "Off") with its on/off button, the two times and the temperature it warms to.
internal sealed class WarmLightItem : IDisposable
{
    private const string TurnOn = "Turn on", TurnOff = "Turn off";

    private readonly WarmLightModel _model;
    private readonly ButtonItem _state;
    private readonly ChoiceItem _start, _end;
    private readonly SliderItem _target;

    public IReadOnlyList<IEditorItem> Cards { get; }

    public WarmLightItem(WarmLightModel model, Action<string> report)
    {
        _model = model;
        var names = WarmLightModel.TimeNames;
        _state = new ButtonItem(WarmLightModel.Label, ButtonText, model.StateText, model.Toggle);
        _start = new ChoiceItem(WarmLightModel.StartLabel, names, model.StartName, model.SetStart) { Half = true };
        _end = new ChoiceItem(WarmLightModel.EndLabel, names, model.EndName, model.SetEnd) { Half = true };
        _target = new SliderItem(WarmLightModel.TargetLabel, model.Kelvin, model.SetKelvin, report, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral,
            " K", new WarmLightSettings().Kelvin) { Half = true };
        Cards = new IEditorItem[] { _state, _start, _end, _target };
        model.Changed += Show;
    }

    private string ButtonText => _model.IsOn ? TurnOff : TurnOn;

    private void Show()
    {
        _state.Description = _model.StateText;
        _state.ButtonText = ButtonText;
        if (_start.Selected != _model.StartName) _start.Load(WarmLightModel.TimeNames, _model.StartName);
        if (_end.Selected != _model.EndName) _end.Load(WarmLightModel.TimeNames, _model.EndName);
        CardMirror.Show(_target, _model.Kelvin, null);
    }

    public void Dispose() => _model.Changed -= Show;
}
