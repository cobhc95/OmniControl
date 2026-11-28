using System.Collections.ObjectModel;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Displays.Pages;

/// The redesigned Night light panel, a view of <see cref="WarmLightModel"/> made with the page:
/// Row 1 (paired Half=true): Night light live toggle + Temperature slider.
/// Row 2 (full width): Schedule night light toggle.
/// Row 3 (paired Half=true, shown only while Schedule is On): Turn on at + Turn off at.
/// Row 4: the muted rule line while its automation rule has extra conditions/triggers.
internal sealed class WarmLightItem : IDisposable
{
    private readonly WarmLightModel _model;
    private readonly ToggleItem _night, _schedule;
    private readonly SliderItem _target;
    private readonly ChoiceItem _start, _end;
    private readonly RuleLineItem _rule;
    private readonly ObservableCollection<object> _cards = new();

    public ItemGroup Group { get; }

    public IReadOnlyList<object> Cards => _cards;

    public WarmLightItem(WarmLightModel model, Action<string> report)
    {
        _model = model;
        _night = new ToggleItem(DisplayCards.WarmLight, model.IsOn, model.SetOn) { Half = true, Note = model.NightNote };
        _target = new SliderItem(DisplayCards.WarmTemperature, model.Kelvin, model.SetKelvin, report, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral,
            " K", new WarmLightSettings().Kelvin) { Half = true };
        _schedule = new ToggleItem(DisplayCards.WarmSchedule, model.IsScheduled, model.SetScheduled) { Note = model.ScheduleNote };
        _start = new ChoiceItem(DisplayCards.WarmFrom, model.StartName, model.SetStart) { Half = true };
        _end = new ChoiceItem(DisplayCards.WarmUntil, model.EndName, model.SetEnd) { Half = true };
        _rule = new RuleLineItem(model.EditRule);
        _rule.Show(model.Rule);
        RebuildCards();
        Group = new ItemGroup("Displays/NightLight", WarmLightModel.Label, _cards)
        {
            ExpandedByDefault = true,
            NoMasterSwitch = true,
            SummaryBesideName = true,
            Summary = model.NightNote,
            Icon = IconLibrary.Moon,
            Restore = model.Reset,
            RestoreTip = "Turns Night light and its schedule off, and puts the times and temperature back to their defaults.",
        };
        model.Changed += Show;
    }

    private void RebuildCards()
    {
        var hasTimes = _cards.Contains(_start);
        if (_cards.Count == 0)
        {
            _cards.Add(_night);
            _cards.Add(_target);
            _cards.Add(_schedule);
            if (_model.IsScheduled)
            {
                _cards.Add(_start);
                _cards.Add(_end);
            }
            _cards.Add(_rule);
            return;
        }
        if (_model.IsScheduled && !hasTimes)
        {
            var at = _cards.IndexOf(_rule);
            _cards.Insert(at, _start);
            _cards.Insert(at + 1, _end);
        }
        else if (!_model.IsScheduled && hasTimes)
        {
            _cards.Remove(_start);
            _cards.Remove(_end);
        }
    }

    private void Show()
    {
        _night.Load(_model.IsOn);
        _night.Note = _model.NightNote;
        _schedule.Load(_model.IsScheduled);
        _schedule.Note = _model.ScheduleNote;
        if (_start.Selected != _model.StartName) _start.Load(WarmLightModel.TimeNames, _model.StartName);
        if (_end.Selected != _model.EndName) _end.Load(WarmLightModel.TimeNames, _model.EndName);
        CardMirror.Show(_target, _model.Kelvin, null);
        _rule.Show(_model.Rule);
        RebuildCards();
        Group.Summary = _model.NightNote;
    }

    public void Dispose()
    {
        _model.Changed -= Show;
        Group.Dispose();
    }
}
