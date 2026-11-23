using OmniControl.App.Features.Icons;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Tray;

/// A slider or tile of the flyout and what it shows: the stored element it comes from and the action and state found for it (what its menu removes, moves, replaces and gives another icon).
public interface ITrayPanelItem
{
    TrayTarget Target { get; }
    bool IsSlider { get; }
}

/// One slider row of the flyout: a level state's short name (or, with a choice state, a pick list of its values), its section's icon, the
/// slider and the value. Moving it sets the state the way the state's own page does; a change from anywhere else moves it (<see cref="Read"/>).
public sealed partial class TraySliderItem : ObservableObject, ITrayPanelItem
{
    /// The chip's words for every display of a together row and for every speaker, and the first row of the speaker's menu (followed by the default's name).
    private const string AllDisplaysWords = "All displays", AllSpeakersWords = "All speakers", DefaultWords = "Windows default", DefaultMark = "default";

    private readonly StateEntry _state;
    private readonly IReadOnlyList<StateEntry> _levels; // the one level state, or every display a together row holds
    private readonly bool _together;
    private readonly StateEntry? _choice;
    private readonly StateEntry? _allLevel; // moves every output device at once
    private readonly Func<string, StateValue?> _now;
    private readonly Action<string?> _savePick;
    private string? _pick; // together: the chosen level state's id; speaker: the pinned device's name or TrayElement.AllPick; null = all displays / follows Windows' default
    private string _lastDefault = string.Empty;
    private bool _syncing;

    /// <param name="savePick">Stores the row's choice (<see cref="TrayElement.Pick"/>) where the panel keeps its elements.</param>
    public TraySliderItem(TrayTarget target, IconImage? icon, Func<string, StateValue?> now, Action<string?> savePick)
    {
        Target = target;
        _state = target.State!;
        _together = target.Group is { Count: > 0 };
        _levels = _together ? target.Group! : new[] { _state };
        _choice = target.Choice;
        _allLevel = target.AllLevel;
        _now = now;
        _savePick = savePick;
        _pick = target.Element.Pick;
        Name = target.Name;
        Icon = icon;
        Read(now);
    }

    /// How far an arrow or +/- key moves the value: the slider's own small change.
    public const int KeyStep = 1;

    public TrayTarget Target { get; }
    public bool IsSlider => true;
    public string Id => _state.Id;
    public string Name { get; }
    public IconImage? Icon { get; }
    public int Minimum => _state.Min;
    public int Maximum => _state.Max;

    /// False for a level that can only be read (the slider is greyed).
    public bool CanSet => Selected().All(s => s.Set is not null);

    public bool HasChoice => _choice is not null || _together;
    public bool HasName => !HasChoice;

    /// The choice state's value in force, as the state words it (the full device name).
    private string FullChoice { get; set; } = string.Empty;

    /// What the chip shows: the display's name or "All displays", or the speaker's short name or "All speakers"; trimming at the column is the last resort.
    [ObservableProperty] private string _choiceText = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValueText))] private int _value;

    public string ValueText => $"{Value}";

    /// The pick list as menu rows (the one in force ticked); built when it opens.
    public IReadOnlyList<MenuRowItem> ChoiceRows => _together ? DisplayRows() : _choice is { } choice ? SpeakerRows(choice) : Array.Empty<MenuRowItem>();

    private IReadOnlyList<MenuRowItem> DisplayRows() =>
        new[] { Row(AllDisplaysWords, Chosen() is null, () => PickDisplay(null), false) }
            .Concat(_levels.Select((level, i) => Row(level.ShortName, Chosen() == level, () => PickDisplay(level.Id), i == 0, level.Note ?? string.Empty))).ToList();

    // A device row: its short name, and under it, muted, its adapter and whether it is Windows' default.
    private IReadOnlyList<MenuRowItem> SpeakerRows(StateEntry choice)
    {
        var rows = new List<MenuRowItem> { Row($"{DefaultWords} · {DeviceNames.Short(FullChoice)}", _pick is null, FollowDefault, false) };
        if (_allLevel is not null) rows.Add(Row(AllSpeakersWords, IsAllSpeakers, PickAllSpeakers, false));
        rows.AddRange((choice.Choices?.Invoke() ?? Array.Empty<string>()).Select((name, i) => Row(DeviceNames.Short(name), name == _pick, () => PickDevice(choice, name), i == 0,
            string.Join(" · ", new[] { DeviceNames.Adapter(name), name == FullChoice ? DefaultMark : string.Empty }.Where(s => s.Length > 0)), name)));
        return rows;
    }

    private static MenuRowItem Row(string text, bool ticked, Action run, bool ruleAbove, string detail = "", string tip = "") =>
        new(text, string.Empty, ticked, tip, new RelayCommand(run), Detail: detail, RuleAbove: ruleAbove);

    private bool IsAllSpeakers => _allLevel is not null && _pick == TrayElement.AllPick;

    /// The level states the slider moves: every output device, or the chosen display, else every display the row holds.
    private IReadOnlyList<StateEntry> Selected() => IsAllSpeakers ? new[] { _allLevel! } : Chosen() is { } one ? new[] { one } : _levels;

    private StateEntry? Chosen() => _together && _pick is { } id ? _levels.FirstOrDefault(s => s.Id == id) : null;

    private void PickDisplay(string? id)
    {
        SetPick(id);
        Read(_now);
    }

    private void FollowDefault()
    {
        SetPick(null);
        Read(_now);
    }

    private void PickAllSpeakers()
    {
        SetPick(TrayElement.AllPick);
        Read(_now);
    }

    private void PickDevice(StateEntry choice, string name)
    {
        SetPick(name);
        choice.Set?.Invoke(StateValue.Of(name));
        Read(_now);
    }

    private void SetPick(string? pick)
    {
        if (_pick == pick) return;
        _pick = pick;
        _savePick(pick);
    }

    /// Takes the states' readings now, without setting anything.
    public void Read(Func<string, StateValue?> now)
    {
        _syncing = true;
        var numbers = Selected().Select(s => now(s.Id)?.Number).OfType<double>().ToList();
        if (numbers.Count > 0) Value = (int)Math.Round(numbers.Average());
        if (_together) ChoiceText = Chosen()?.ShortName ?? AllDisplaysWords;
        else if (_choice is { } choice) ReadDevice(choice, now(choice.Id));
        _syncing = false;
        OnPropertyChanged(nameof(CanSet));
    }

    // The default-follow rule: a pinned device stays pinned while it is the default; when Windows' default changes to anything else the row follows it again.
    private void ReadDevice(StateEntry choice, StateValue? reading)
    {
        FullChoice = choice.Words(reading);
        if (reading is not null)
        {
            if (_pick is not null && !IsAllSpeakers && FullChoice != _lastDefault && FullChoice != _pick) SetPick(null);
            _lastDefault = FullChoice;
        }
        ChoiceText = IsAllSpeakers ? AllSpeakersWords : DeviceNames.Short(FullChoice);
    }

    /// True when a change of this state matters to the row.
    public bool Shows(string stateId) => _levels.Any(s => s.Id == stateId) || stateId == _choice?.Id || stateId == _allLevel?.Id;

    partial void OnValueChanged(int value)
    {
        if (_syncing) return;
        foreach (var level in Selected()) level.Set?.Invoke(StateValue.Of(value));
    }
}

/// The one small mark at a tile's corner: nothing, an action running, the state being read, only the last known value, or a failure.
public enum TileMark { None, Acting, Reading, NotSure, Failed }

/// One tile of the flyout: its section's icon and short name. A click runs its action (through the master list, so a block rule still
/// applies), or, for an on/off state alone, sets it to the other value. It always shows the state's real or last-read value (lit while on)
/// and never lights because it was clicked; its <see cref="Mark"/> says what is happening to that value.
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


    [ObservableProperty] private bool _isOn;

    /// The corner mark: what the state's owner is doing with it or how far it can be trusted; never lights the tile.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsActing), nameof(IsReading), nameof(IsNotSure), nameof(IsFailed), nameof(Reason))] private TileMark _mark;

    /// Why a failed action or read failed, one plain sentence; shown in the tile's right-click menu, never on hover.
    public string? Failure { get; private set; }

    private string? _notSure;

    public bool IsActing => Mark == TileMark.Acting;
    public bool IsReading => Mark == TileMark.Reading;
    public bool IsNotSure => Mark == TileMark.NotSure;
    public bool IsFailed => Mark == TileMark.Failed;

    /// The hover words, only for a state that is not sure ("Last read 08:39"); null for every other mark.
    public string? Reason => IsNotSure ? _notSure : null;

    /// How its state can be read again on request; null when it needs none.
    public StateRecheck? Recheck => _target.State?.Recheck;

    public void Read(Func<string, StateValue?> now)
    {
        IsOn = _target.State is { } state && _target.IsOn(now(state.Id));
        var live = _target.State?.Liveness?.Invoke();
        Failure = live?.Failure;
        _notSure = live?.NotSure;
        Mark = live switch
        {
            null => TileMark.None,
            { Acting: true } => TileMark.Acting,
            { Reading: true } => TileMark.Reading,
            { Failure: not null } => TileMark.Failed,
            { NotSure: not null } => TileMark.NotSure,
            _ => TileMark.None,
        };
    }

    public bool Shows(string stateId) => _target.State?.Id == stateId;

    [RelayCommand]
    private void Run()
    {
        if (_target.Action is { } action) _runAction(action.Id);
        else _target.State?.Set?.Invoke(StateValue.Of(!IsOn));
    }
}
