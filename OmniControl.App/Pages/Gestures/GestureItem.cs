using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Gestures;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;

namespace OmniControl.App.Pages.Gestures;

/// <summary>One gesture's half-width card: a switch, the trigger's name, a pill showing the action the wheel or middle click drives, and a cogwheel for its settings.</summary>
public sealed partial class GestureItem : ObservableObject, IEditorItem, IHalfWidth, IPickerCard
{
    private readonly GestureRule _rule;
    private readonly Action _changed;
    private readonly Action<GestureItem, PickResult> _applyToOthers;
    private readonly Func<IReadOnlyList<PickChoice>> _choiceSource;
    private IReadOnlyList<PickChoice> _choices;
    private string _parameter; // the text a middle-click action takes (a shortcut, a program); wheel steps take none
    private bool _loading;

    public string Label { get; }

    /// <summary>Two gestures share a row.</summary>
    public bool Half => true;

    [ObservableProperty] private bool _isOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasOptions))]
    private PickChoice _choice;

    /// <summary>The name of the action, as shown on the card's pill.</summary>
    public string Summary => Choice.Name;

    /// <summary>True when the action has settings (the card shows a cogwheel).</summary>
    public bool HasOptions => Choice.HasOptions;

    /// <summary>The values of the action's settings (only those the user changed from the default).</summary>
    public IReadOnlyDictionary<string, string> Values => _rule.Options;

    /// <param name="choices">The action list as it is now (it grows as features register OmniControl actions).</param>
    /// <param name="rule">The saved state this card edits.</param>
    /// <param name="changed">Called after any change, so the settings are saved and the hook gets a fresh copy.</param>
    /// <param name="applyToOthers">Called when the user chose to give every gesture with the same action these settings.</param>
    public GestureItem(string label, Func<IReadOnlyList<PickChoice>> choices, GestureRule rule, Action changed, Action<GestureItem, PickResult> applyToOthers)
    {
        Label = label;
        _choiceSource = choices;
        _choices = choices();
        _rule = rule;
        _changed = changed;
        _applyToOthers = applyToOthers;
        _isOn = rule.On;
        _choice = ChoiceOfRule();
        _parameter = _choice.ParameterOf(rule.Action);
    }

    /// <summary>The action list changed (a feature registered an action): shows the stored action from the new list, without saving.</summary>
    public void Reload()
    {
        _choices = _choiceSource();
        _loading = true;
        Choice = ChoiceOfRule();
        _parameter = Choice.ParameterOf(_rule.Action);
        _loading = false;
    }

    private PickChoice ChoiceOfRule() => _choices.FirstOrDefault(c => c.Matches(_rule.Action)) ?? _choices[0];

    /// <summary>The picker also offers to give every gesture that uses the action the same settings.</summary>
    public PickerModel MenuFor(bool startInSettings) =>
        new(PickChoice.Group(_choices), Choice, Values, startInSettings, _parameter, offerApplyToAll: true);

    /// <summary>Takes the action and settings chosen in the action window.</summary>
    public void Pick(PickResult pick)
    {
        _rule.Options = OptionValues.NonDefault(pick.Choice.Options, pick.Values);
        if (pick.Parameter is not null) _parameter = pick.Parameter;
        if (pick.ApplyToAll) _applyToOthers(this, pick);
        if (Choice != pick.Choice)
        {
            Choice = pick.Choice;
            return;
        }
        _rule.Action = Choice.WithParameter(_parameter); // same action, new settings or text
        _changed();
    }

    /// <summary>Takes the settings another gesture was given, when it uses the same action.</summary>
    public void TakeSettingsOf(PickResult pick)
    {
        if (Choice != pick.Choice) return;
        _rule.Options = OptionValues.NonDefault(pick.Choice.Options, pick.Values);
    }

    partial void OnIsOnChanged(bool value)
    {
        _rule.On = value;
        _changed();
    }

    partial void OnChoiceChanged(PickChoice value)
    {
        if (_loading) return;
        _rule.Action = value.WithParameter(_parameter);
        _changed();
    }
}
