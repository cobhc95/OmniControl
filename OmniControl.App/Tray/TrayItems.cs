using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>A slider or tile of the flyout and what it shows: the stored element it comes from and the action and state found for it (what its menu removes, moves, replaces and gives another icon).</summary>
public interface ITrayPanelItem
{
    TrayTarget Target { get; }
    bool IsSlider { get; }
}

/// <summary>
/// One slider row of the flyout: a level state's short name (or, with a choice state, a pick list of its values), its section's icon, the
/// slider and the value. Moving it sets the state the way the state's own page does; a change from anywhere else moves it (<see cref="Read"/>).
/// </summary>
public sealed partial class TraySliderItem : ObservableObject, ITrayPanelItem
{
    private readonly StateEntry _state;
    private readonly StateEntry? _choice;
    private bool _syncing;

    public TraySliderItem(TrayTarget target, IconImage? icon, Func<string, StateValue?> now)
    {
        Target = target;
        _state = target.State!;
        _choice = target.Choice;
        Name = target.Name;
        Icon = icon;
        Read(now);
    }

    public TrayTarget Target { get; }
    public bool IsSlider => true;
    public string Id => _state.Id;
    public string Name { get; }
    public IconImage? Icon { get; }
    /// <summary>The state's name, and the full device name when the chip shows its short one.</summary>
    public string Tip => HasChoice && FullChoice.Length > 0 ? $"{_state.Name} - {FullChoice}" : _state.Name;
    public int Minimum => _state.Min;
    public int Maximum => _state.Max;

    /// <summary>False for a level that can only be read (the slider is greyed).</summary>
    public bool CanSet => _state.Set is not null;

    public bool HasChoice => _choice is not null;
    public bool HasName => !HasChoice;

    /// <summary>The choice state's value in force, as the state words it (the full device name).</summary>
    private string FullChoice { get; set; } = string.Empty;

    /// <summary>What the chip shows: the device's short name; trimming at the column is the last resort.</summary>
    [ObservableProperty] private string _choiceText = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValueText))] private int _value;

    public string ValueText => $"{Value}";

    /// <summary>The choice state's values as menu rows (the one in force ticked); built when the pick list opens.</summary>
    public IReadOnlyList<MenuRowItem> ChoiceRows =>
        _choice is not { } choice ? Array.Empty<MenuRowItem>()
            : (choice.Choices?.Invoke() ?? Array.Empty<string>()).Select(name =>
                new MenuRowItem(name, string.Empty, name == FullChoice, string.Empty, new RelayCommand(() => choice.Set?.Invoke(StateValue.Of(name))))).ToList();

    /// <summary>Takes the states' readings now, without setting anything.</summary>
    public void Read(Func<string, StateValue?> now)
    {
        _syncing = true;
        if (now(_state.Id)?.Number is { } number) Value = (int)Math.Round(number);
        if (_choice is { } choice)
        {
            FullChoice = choice.Words(now(choice.Id));
            ChoiceText = DeviceNames.Short(FullChoice);
        }
        _syncing = false;
    }

    /// <summary>True when a change of this state matters to the row.</summary>
    public bool Shows(string stateId) => stateId == _state.Id || stateId == _choice?.Id;

    partial void OnValueChanged(int value)
    {
        if (!_syncing) _state.Set?.Invoke(StateValue.Of(value));
    }
}

/// <summary>
/// One tile of the flyout: its section's icon and short name. A click runs its action (through the master list, so a block rule still
/// applies), or, for an on/off state alone, sets it to the other value. It is lit while its state is on.
/// </summary>
public sealed partial class TrayTileItem : ObservableObject, ITrayPanelItem
{
    private readonly TrayTarget _target;
    private readonly Action<string> _runAction;

    public TrayTileItem(TrayTarget target, IconImage? icon, Func<string, StateValue?> now, Action<string> runAction)
    {
        _target = target;
        _runAction = runAction;
        Name = target.Name;
        Icon = icon;
        Read(now);
    }

    public TrayTarget Target => _target;
    public bool IsSlider => false;
    public string Name { get; }
    public IconImage? Icon { get; }

    /// <summary>The full name (a tile's words are short and may be trimmed).</summary>
    public string Tip => _target.Action?.Title ?? _target.State?.Name ?? Name;

    [ObservableProperty] private bool _isOn;

    /// <summary>Its state is being read again now (the corner dot).</summary>
    [ObservableProperty] private bool _isChecking;

    /// <summary>Its state is only the last one known and nothing cheap can read it again (a device run by simulation only): drawn faded.</summary>
    [ObservableProperty] private bool _isFaded;

    /// <summary>How its state can be read again on request; null when it needs none.</summary>
    public StateRecheck? Recheck => _target.State?.Recheck;

    public void Read(Func<string, StateValue?> now)
    {
        IsOn = _target.State is { } state && now(state.Id)?.Number is { } number && number != 0;
        IsFaded = Recheck is { } recheck && !recheck.CanCheck();
    }

    public bool Shows(string stateId) => _target.State?.Id == stateId;

    [RelayCommand]
    private void Run()
    {
        if (_target.Action is { } action) _runAction(action.Id);
        else _target.State?.Set?.Invoke(StateValue.Of(!IsOn));
    }
}

/// <summary>The dashed tile after the others: adds a tile through the Choose-the-action window.</summary>
public sealed class TrayAddTile
{
    public static TrayAddTile Instance { get; } = new();

    private TrayAddTile() { }

    public string Text => "+ Add";
    public string Tip => "Adds a tile: any action, or anything that is on or off.";
}
