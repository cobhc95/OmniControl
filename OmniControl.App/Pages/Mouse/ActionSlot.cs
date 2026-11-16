using OmniControl.App.Hotkeys;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// One of a card's actions (a chip, and a row of the "+N more" list): its entry, text and settings, its number in the running order,
/// and what it can do (open its own picker, move, be taken away). The card (<see cref="MouseButtonItem"/>) owns the list and decides
/// everything; the slot only reports through the callbacks it was given, by its position.
/// </summary>
public sealed partial class ActionSlot : ObservableObject, IPickerCard
{
    /// <summary>What the card does for a slot: open a picker on it, take a pick, remove it, move it by a step.</summary>
    public sealed record Callbacks(Func<int, bool, PickerModel> Menu, Action<int, PickResult> Pick, Action<int> Remove, Action<int, int> Move);

    private const string MissingName = "(an action that is no longer available)";

    private readonly Callbacks _card;
    private string? _missing; // the stored text of an action no list entry matches now, kept as it is

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(HasOptions))]
    private PickChoice _choice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _parameter = string.Empty;

    /// <summary>Position in the list, from 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number))]
    private int _index;

    /// <summary>True when the card has two or more actions, so the chips show their numbers.</summary>
    [ObservableProperty] private bool _showsNumber;

    [ObservableProperty] private bool _canMoveUp;
    [ObservableProperty] private bool _canMoveDown;

    /// <summary>The picker window's title names the card (and the action's number when there are several).</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>The values of the action's settings (only those changed from the default).</summary>
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public int Number => Index + 1;
    public string Name => _missing is null ? Choice.Name : MissingName;
    public bool HasDetail => Parameter.Length > 0;
    public bool HasOptions => _missing is null && Choice.HasOptions;

    /// <summary>The id of the stored action ("keys", "layer.next"...), which decides where in the list it may stand.</summary>
    public string Id => MouseAction.Parse(Spec).Id;

    /// <summary>The stored text of this action.</summary>
    public string Spec => _missing ?? MouseAction.Compose(Choice.WithParameter(Parameter), Choice.Options, Values);

    internal ActionSlot(PickChoice choice, Callbacks card)
    {
        _choice = choice;
        _card = card;
    }

    /// <summary>Shows a stored action: <paramref name="choice"/> is the entry it matches, or null when none does (it is then kept as it is).</summary>
    internal void Show(PickChoice? choice, PickChoice fallback, string stored)
    {
        var (action, values) = MouseAction.Split(stored);
        _missing = choice is null ? stored : null;
        Choice = choice ?? fallback;
        Parameter = choice?.ParameterOf(action) ?? string.Empty;
        Values = values;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasOptions));
    }

    /// <summary>Takes a pick: the entry, its settings and (when the settings page gave it) its text.</summary>
    internal void Take(PickResult pick)
    {
        _missing = null;
        Values = OptionValues.NonDefault(pick.Choice.Options, pick.Values);
        if (pick.Parameter is not null) Parameter = pick.Parameter;
        Choice = pick.Choice;
        OnPropertyChanged(nameof(Name));
    }

    public PickerModel MenuFor(bool startInSettings) => _card.Menu(Index, startInSettings);

    public void Pick(PickResult pick) => _card.Pick(Index, pick);

    [RelayCommand] private void Remove() => _card.Remove(Index);

    [RelayCommand] private void MoveUp() => _card.Move(Index, -1);

    [RelayCommand] private void MoveDown() => _card.Move(Index, 1);
}
