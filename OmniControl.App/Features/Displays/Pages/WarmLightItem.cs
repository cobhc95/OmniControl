namespace OmniControl.App.Features.Displays.Pages;

/// The warm-light schedule's cards, a view of the <see cref="WarmLightModel"/> made with the page: one line that says honestly what it is
/// doing ("Active until 07:00", "Starts at 21:00", "Off") with its on/off button, the two times and the temperature it warms to, then the
/// muted line while its automation rule has more than these cards show.
internal sealed class WarmLightItem : IDisposable
{
    private readonly WarmLightModel _model;
    private readonly ButtonItem _state;
    private readonly ChoiceItem _start, _end;
    private readonly SliderItem _target;
    private readonly RuleLineItem _rule;

    public IReadOnlyList<IEditorItem> Cards { get; }

    public WarmLightItem(WarmLightModel model, Action<string> report)
    {
        _model = model;
        _state = new ButtonItem(DisplayCards.WarmLight, ButtonText, model.Toggle) { Description = model.StateText };
        _start = new ChoiceItem(DisplayCards.WarmFrom, model.StartName, model.SetStart) { Half = true };
        _end = new ChoiceItem(DisplayCards.WarmUntil, model.EndName, model.SetEnd) { Half = true };
        _target = new SliderItem(DisplayCards.WarmTemperature, model.Kelvin, model.SetKelvin, report, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral,
            " K", new WarmLightSettings().Kelvin) { Half = true };
        _rule = new RuleLineItem(model.EditRule);
        _rule.Show(model.Rule);
        Cards = new IEditorItem[] { _state, _start, _end, _target, _rule };
        model.Changed += Show;
    }

    private string ButtonText => _model.IsOn ? WarmLightModel.TurnOff : WarmLightModel.TurnOn;

    private void Show()
    {
        _state.Description = _model.StateText;
        _state.ButtonText = ButtonText;
        if (_start.Selected != _model.StartName) _start.Load(WarmLightModel.TimeNames, _model.StartName);
        if (_end.Selected != _model.EndName) _end.Load(WarmLightModel.TimeNames, _model.EndName);
        CardMirror.Show(_target, _model.Kelvin, null);
        _rule.Show(_model.Rule);
    }

    public void Dispose() => _model.Changed -= Show;
}
