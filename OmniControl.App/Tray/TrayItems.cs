using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>
/// One slider row of the flyout: a level state's short name (or, with a choice state, a pick list of its values), its section's icon, the
/// slider and the value. Moving it sets the state the way the state's own page does; a change from anywhere else moves it (<see cref="Read"/>).
/// </summary>
public sealed partial class TraySliderItem : ObservableObject
{
    private readonly StateEntry _state;
    private readonly StateEntry? _choice;
    private bool _syncing;

    public TraySliderItem(TrayTarget target, string? icon, Func<string, StateValue?> now)
    {
        _state = target.State!;
        _choice = target.Choice;
        Name = target.Name;
        Icon = icon;
        Read(now);
    }

    public string Id => _state.Id;
    public string Name { get; }
    public string? Icon { get; }
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
public sealed partial class TrayTileItem : ObservableObject
{
    private readonly TrayTarget _target;
    private readonly Action<string> _runAction;

    public TrayTileItem(TrayTarget target, string? icon, Func<string, StateValue?> now, Action<string> runAction)
    {
        _target = target;
        _runAction = runAction;
        Name = target.Name;
        Icon = icon;
        Read(now);
    }

    public string Name { get; }
    public string? Icon { get; }

    /// <summary>The full name (a tile's words are short and may be trimmed).</summary>
    public string Tip => _target.Action?.Title ?? _target.State?.Name ?? Name;

    [ObservableProperty] private bool _isOn;

    public void Read(Func<string, StateValue?> now) => IsOn = _target.State is { } state && now(state.Id)?.Number is { } number && number != 0;

    public bool Shows(string stateId) => _target.State?.Id == stateId;

    [RelayCommand]
    private void Run()
    {
        if (_target.Action is { } action) _runAction(action.Id);
        else _target.State?.Set?.Invoke(StateValue.Of(!IsOn));
    }
}

/// <summary>One element in the flyout's edit list: its icon, what it shows now (several names for an id with a <c>*</c>), whether it shows at all, and its remove.</summary>
public sealed class TrayEditRow
{
    public TrayEditRow(TrayElement element, IReadOnlyList<TrayTarget> targets, string? icon, Action remove)
    {
        Element = element;
        Icon = icon;
        Name = targets.Count == 0 ? element.State ?? element.Action ?? string.Empty : string.Join(", ", targets.Select(t => t.Name));
        IsMissing = targets.Count == 0;
        RemoveCommand = new RelayCommand(remove);
    }

    public TrayElement Element { get; }
    public string? Icon { get; }
    public string Name { get; }

    /// <summary>Nothing it names is registered now (a removed device, a monitor not attached): it is kept but not shown.</summary>
    public bool IsMissing { get; }

    public string MissingText => "Not available now";
    public string MissingTip => "Nothing it names is there now (a device removed, a monitor not attached). It shows again when it is back.";
    public string RemoveTip => "Remove from the flyout";

    public IRelayCommand RemoveCommand { get; }
}

/// <summary>The dashed tile after the others: adds a tile through the Choose-the-action window.</summary>
public sealed class TrayAddTile
{
    public static TrayAddTile Instance { get; } = new();

    private TrayAddTile() { }

    public string Text => "+ Add";
    public string Tip => "Adds a tile: any action, or anything that is on or off.";
}
