using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Displays.Pages;

/// One display's own controls as the page's groups of cards: a view of its <see cref="HardwareDisplayModel"/>, made with the page. Until the
/// display has answered, every card waits, greyed, in one group; then the page shows the cards it has, and the ones it does not report
/// wait, greyed and with the reason, in a second group that is collapsed.
internal sealed class HardwareDisplayItem : IDisposable
{
    private const string Waiting = ChoiceState.Waiting;
    private const string UnavailableName = "Not available on ";

    private readonly HardwareDisplayModel _model;
    private readonly Dictionary<HardwareLevel, SliderItem> _sliders = new();
    private readonly Dictionary<HardwareChoice, ChoiceItem> _choices = new();
    private readonly DisplayOrientationCard _orientation;
    private ItemGroup? _main;

    public string Name => _model.Name;

    /// What the page shows for this display: its group, and after the display was read a collapsed group of what it does not report.
    public IReadOnlyList<ItemGroup> Groups { get; private set; }

    /// Every card this display could have, whether it reports the control or not.
    public IReadOnlyList<IEditorItem> Cards { get; }

    /// The cards the display has (none until it has been read).
    public IEnumerable<IEditorItem> AvailableCards => Cards.Where(c => AvailabilityOf(c).IsAvailable);

    /// <param name="rotationKeys">The switch of the orientation hotkeys' suggested keys, on the Orientation card (which owns it).</param>
    public HardwareDisplayItem(HardwareDisplayModel model, Action<string> report, SuggestedKeysSwitch rotationKeys)
    {
        _model = model;
        var cards = new List<IEditorItem>();
        foreach (var level in DisplayControls.Hardware)
        {
            var spec = level.Spec;
            var slider = new SliderItem(spec.Label, model.Level(level).Value, v => Set(level, v), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
            _sliders[level] = slider;
            cards.Add(slider);
        }
        foreach (var choice in DisplayControls.HardwareChoices)
        {
            var card = new ChoiceItem(choice.Feature.Label, new[] { Waiting }, Waiting, picked => model.Pick(choice, picked)) { Half = true };
            _choices[choice] = card;
            cards.Add(card);
        }
        _orientation = new DisplayOrientationCard(model.Orientation, rotationKeys);
        cards.Add(_orientation.Card);
        Cards = cards;
        Groups = new[] { new ItemGroup(GroupKey, Name, Cards.Cast<object>().ToList()) { Summary = Waiting } };
        model.Changed += ShowValues;
        Show();
    }

    private string GroupKey => $"Displays/Hardware/{_model.Id}";

    private OperationResult Set(HardwareLevel level, int value)
    {
        _model.Set(level, value);
        return OperationResult.Ok();
    }

    private static Availability AvailabilityOf(IEditorItem card) => card switch
    {
        SliderItem s => s.Availability,
        ChoiceItem c => c.Availability,
        GalleryItem g => g.Availability,
        _ => throw new ArgumentException("Not a card with an availability.", nameof(card)),
    };

    /// The display came or was read: the cards show its values, and once it was read its groups split into what it has and what it does not.
    public void Show()
    {
        ShowValues();
        if (_main is not null || !_model.IsRead) return;
        var shown = AvailableCards.Cast<object>().ToList();
        var hidden = Cards.Where(c => !AvailabilityOf(c).IsAvailable).Cast<object>().ToList();
        _main = new ItemGroup(GroupKey, Name, shown) { Summary = $"{shown.Count} available", Tip = _model.HasScreenPower ? null : NoPowerTip };
        if (_model.HasScreenPower) _main.UseSwitch(ScreenPowerSwitch.One(new ScreenPowerSwitch.Screen(() => _model.ScreenOn, _model.SetScreenOn)));
        var groups = new List<ItemGroup> { _main };
        if (hidden.Count > 0)
            groups.Add(new ItemGroup($"{GroupKey}/unavailable", UnavailableName + Name, hidden) { Summary = $"{hidden.Count} not reported" });
        Groups = groups;
    }

    /// The screen's power may have changed: its group's switch shows it again.
    public void RefreshMaster() => _main?.RefreshMaster();

    private void ShowValues()
    {
        foreach (var (level, slider) in _sliders)
        {
            var state = _model.Level(level);
            slider.ResetValue = state.ResetValue;
            CardMirror.Show(slider, state.Value, state.Reason);
        }
        foreach (var (choice, card) in _choices)
        {
            var state = _model.Choice(choice);
            if (state.IsAvailable && (card.Selected != state.Selected || !card.Options.SequenceEqual(state.Names))) card.Load(state.Names, state.Selected);
            CardMirror.Show(card.Availability, state.Reason);
        }
    }

    private string NoPowerTip => _model.IsLaptopPanel
        ? "No power switch for this screen: Windows has no way to turn the built-in panel off and on on its own. Use Turn off displays for all screens."
        : "No power switch for this screen: the monitor does not report DDC power control.";

    public void Dispose()
    {
        _model.Changed -= ShowValues;
        _orientation.Dispose();
    }
}
