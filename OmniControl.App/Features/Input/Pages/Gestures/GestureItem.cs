using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Input.Pages.Gestures;

/// One gesture's half-width card: a switch, the trigger's name, a pill showing the action the wheel or middle click drives, and a cogwheel for its settings.
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

    /// Two gestures share a row.
    public bool Half => true;

    [ObservableProperty] private bool _isOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasOptions))]
    private PickChoice _choice;

    /// The name of the action, as shown on the card's pill (the keys themselves for "Send keys").
    public string Summary => Choice.ShownName(_parameter);

    /// True when the action has settings (the card shows a cogwheel).
    public bool HasOptions => Choice.HasOptions;

    /// The card's own apps scope (its cogwheel's menu).
    public AppScopeModel Scope { get; }

    /// The tag under the card: its apps, shown only while they differ from the group's.
    public bool ShowsScopeTag => Scope.ShowsTag;

    public string ScopeTagText => Scope.Text;

    /// The values of the action's settings (only those the user changed from the default).
    public IReadOnlyDictionary<string, string> Values => _rule.Options;

    /// <param name="choices">The action list as it is now (it grows as features register OmniControl actions).</param>
    /// <param name="rule">The saved state this card edits.</param>
    /// <param name="changed">Called after any change, so the settings are saved and the hook gets a fresh copy.</param>
    /// <param name="applyToOthers">Called when the user chose to give every gesture with the same action these settings.</param>
    /// <param name="scope">The card's apps scope.</param>
    public GestureItem(string label, Func<IReadOnlyList<PickChoice>> choices, GestureRule rule, Action changed, Action<GestureItem, PickResult> applyToOthers,
        AppScopeModel scope)
    {
        Scope = scope;
        scope.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowsScopeTag));
            OnPropertyChanged(nameof(ScopeTagText));
        };
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

    /// The action list changed (a feature registered an action): shows the stored action from the new list, without saving.
    public void Reload()
    {
        _choices = _choiceSource();
        Scope.Refresh();
        _loading = true;
        var choice = ChoiceOfRule();
        _parameter = choice.ParameterOf(_rule.Action);
        Choice = choice;
        _loading = false;
        OnPropertyChanged(nameof(Summary));
    }

    private PickChoice ChoiceOfRule() => _choices.FirstOrDefault(c => c.Matches(_rule.Action)) ?? _choices[0];

    /// The picker also offers to give every gesture that uses the action the same settings.
    public PickerModel MenuFor(bool startInSettings) =>
        new(PickChoice.Group(_choices), Choice, Values, startInSettings, _parameter, offerApplyToAll: true);

    /// Takes the action and settings chosen in the action window.
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
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    /// Takes the settings another gesture was given, when it uses the same action.
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
