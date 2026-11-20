using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// What a card's Windows tag says: nothing, Windows also runs its own action (asked for), or turning Windows' own one off failed.
public enum WindowsTag { None, AlsoRuns, Problem }

/// One input's half-width card (a mouse button, or a trackpad gesture): its actions in running order (<see cref="Slots"/>, shown as
/// chips), each with its settings and text (a shortcut or a program path). The first action keeps every setting it has today (when it
/// runs, letting the input through); the second and later ones are one-shot actions (<see cref="MouseActionCatalog.ExtraChoices"/>)
/// that run after it. The card owns the list and stores it whole after every change. A trackpad gesture's card can be unavailable
/// (no precision touchpad) and says when Windows also acts on it.
public sealed partial class MouseButtonItem : ObservableObject, IEditorItem, IHalfWidth, IPickerCard
{
    private readonly Func<IReadOnlyList<PickChoice>> _firstSource, _laterSource;
    private IReadOnlyList<PickChoice> _first, _later;
    private readonly Func<IReadOnlyList<string>> _read;
    private readonly Action<IReadOnlyList<string>> _write;
    private readonly ActionSlot.Callbacks _callbacks;
    private readonly ISharedOptions? _shared;

    public MouseButton Button { get; }

    public string Label { get; }

    /// True for a moment after the real button is pressed, so the card lights up.
    [ObservableProperty] private bool _isFlashing;

    private int _flashId;

    /// Lights the card for <see cref="Timing.MouseFlash"/>; a press during the flash restarts it.
    public async void Flash()
    {
        var mine = ++_flashId;
        IsFlashing = true;
        await Task.Delay(Timing.MouseFlash);
        if (mine == _flashId) IsFlashing = false;
    }

    /// Two buttons share a row.
    public bool Half => true;

    /// The actions in the order they run; empty while the input is at Default.
    public ObservableCollection<ActionSlot> Slots { get; } = new();

    /// What the chip row shows: every action's chip, then the "+N more" chip and the dashed add chip (the row's panel folds the rest).
    public ObservableCollection<object> Chips { get; } = new();

    public FoldChip Fold { get; } = new();
    public AddChip Add { get; }

    /// Greyed out with the reason when the input can't be seen now (a trackpad gesture without a precision touchpad).
    public Availability Availability { get; } = new();

    /// What Windows itself does with this input (a trackpad gesture Windows' touchpad settings use); the page keeps it current.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWindowsAlso), nameof(ShowsWindowsProblem))]
    private WindowsTag _windowsState;

    /// The tag's tooltip: what Windows does, or why its own gesture could not be turned off.
    [ObservableProperty] private string _windowsDetail = string.Empty;

    // The card's tags show only once the input has an action of its own, since at "Default" Windows doing its own thing is what is wanted.
    public bool ShowsWindowsAlso => WindowsState == WindowsTag.AlsoRuns && IsCustom;
    public bool ShowsWindowsProblem => WindowsState == WindowsTag.Problem && IsCustom;

    /// True when the first action's "also keep the original" setting is ticked (for a trackpad gesture: Windows' own gesture stays on).
    public bool KeepsOriginal => First is { } first && new OptionValues(first.Values).Bool(MouseOptions.KeepOriginal);

    public const string WindowsAlsoText = "Windows also does this";
    public const string WindowsProblemText = "Windows' own still on";

    /// What the pill says while the input has no action of its own.
    public const string Summary = "Default";

    private ActionSlot? First => Slots.Count > 0 ? Slots[0] : null;

    /// True when the input has been given an action (its chips show); false while it behaves as Windows normally does.
    public bool IsCustom => Slots.Count > 0;

    /// The text the first action takes (a shortcut, a program), edited in the box under the card.
    public string Parameter
    {
        get => First?.Parameter ?? string.Empty;
        set
        {
            if (First is not { } first || first.Parameter == value) return;
            first.Parameter = value;
            Save();
            OnPropertyChanged(nameof(ParameterProblem));
        }
    }

    /// What is wrong with the first action's text (an unknown key name), shown as a caution tag under its box; null when fine.
    public string? ParameterProblem => First?.Choice.ParamCheck?.Invoke(Parameter);

    /// The first action's text is a plain one (a shortcut): a text box.
    public bool NeedsParameter => First?.Choice is { ParamHint: not null, ParamBrowse: null };

    /// The first action's text is a path (a program, a file, a folder): the shared path field with Browse.
    public bool NeedsPath => First?.Choice is { ParamHint: not null, ParamBrowse: not null };
    public PathKind PathKind => First?.Choice.ParamBrowse ?? PathKind.File;
    public string Hint => First?.Choice.ParamHint ?? string.Empty;

    /// True when the first action has settings.
    public bool HasOptions => First?.HasOptions == true;

    /// The card's own apps scope (its cogwheel's menu).
    public AppScopeModel Scope { get; }

    /// The tag under the card: its apps, shown only while they differ from the group's.
    public bool ShowsScopeTag => Scope.ShowsTag;

    public string ScopeTagText => Scope.Text;

    /// Every action's name, so search finds the card by any of them.
    public string SearchWords => string.Join(" ", Slots.Select(s => s.Name));

    /// <param name="firstChoices">The first action's list as it is now (it grows as features register OmniControl actions).</param>
    /// <param name="laterChoices">The list for the second and later actions (one-shot only).</param>
    /// <param name="read">The stored actions of this input right now, in order.</param>
    /// <param name="write">Stores the actions in order.</param>
    /// <param name="scope">The card's apps scope.</param>
    public MouseButtonItem(MouseButton button, string label, Func<IReadOnlyList<PickChoice>> firstChoices, Func<IReadOnlyList<PickChoice>> laterChoices,
        Func<IReadOnlyList<string>> read, Action<IReadOnlyList<string>> write, AppScopeModel scope, ISharedOptions? shared = null)
    {
        _shared = shared;
        Scope = scope;
        scope.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowsScopeTag));
            OnPropertyChanged(nameof(ScopeTagText));
        };
        Button = button;
        Label = label;
        _firstSource = firstChoices;
        _laterSource = laterChoices;
        _first = firstChoices();
        _later = laterChoices();
        _read = read;
        _write = write;
        _callbacks = new ActionSlot.Callbacks(SlotMenu, PickSlot, RemoveSlot, MoveSlot);
        Add = new AddChip(NewMenu, pick => PickSlot(Slots.Count, pick));
        Chips.Add(Fold);
        Chips.Add(Add);
        Reload();
    }

    /// Shows what is stored now (after the layer or the action list changed), without writing anything back.
    public void Reload()
    {
        _first = _firstSource();
        _later = _laterSource();
        Scope.Refresh();
        var stored = _read();
        while (Slots.Count > stored.Count) RemoveChip(Slots.Count - 1);
        for (var i = 0; i < stored.Count; i++)
        {
            var list = ListFor(i);
            var action = MouseAction.Split(stored[i]).Action;
            if (i == Slots.Count) AddChip(new ActionSlot(list[0], _callbacks));
            Slots[i].Show(list.FirstOrDefault(c => c.Matches(action)), list[0], stored[i]);
        }
        Renumber();
    }

    private IReadOnlyList<PickChoice> ListFor(int index) => index == 0 ? _first : _later;

    private void AddChip(ActionSlot slot)
    {
        Chips.Insert(Slots.Count, slot);
        Slots.Add(slot);
    }

    private void RemoveChip(int index)
    {
        Chips.RemoveAt(index);
        Slots.RemoveAt(index);
    }

    /// Numbers, labels, which way each chip may move, the add chip, and the card's first-action properties.
    private void Renumber()
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            slot.Index = i;
            slot.ShowsNumber = Slots.Count > 1;
            slot.Label = Slots.Count > 1 ? $"{Label}, action {i + 1}" : Label;
            slot.CanMoveUp = i > 0 && MayPlace(i, i - 1);
            slot.CanMoveDown = i < Slots.Count - 1 && MayPlace(i, i + 1);
        }
        var stops = First is { } first && first.Id == MouseActionCatalog.Disabled;
        Add.CanAdd = IsCustom && !stops && Slots.Count < MouseSettings.MaxActions;
        Add.Label = $"{Label}, action {Slots.Count + 1}";
        Add.Tip = Slots.Count >= MouseSettings.MaxActions
            ? $"An input can have at most {MouseSettings.MaxActions} actions."
            : "Add an action that runs after the others, each time the input fires.";
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(Parameter));
        OnPropertyChanged(nameof(ParameterProblem));
        OnPropertyChanged(nameof(NeedsParameter));
        OnPropertyChanged(nameof(NeedsPath));
        OnPropertyChanged(nameof(PathKind));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(ShowsWindowsAlso));
        OnPropertyChanged(nameof(ShowsWindowsProblem));
    }

    /// True when the action at <paramref name="from"/> may swap places with the one at <paramref name="to"/>: the second and later places
    /// take only one-shot actions (<see cref="MouseActionCatalog.CanRunLater"/>).
    private bool MayPlace(int from, int to) =>
        (to == 0 || MouseActionCatalog.CanRunLater(Slots[from].Id)) && (from == 0 || MouseActionCatalog.CanRunLater(Slots[to].Id));

    /// The card's own picker (the Default pill, Enter on the card, the cogwheel): the first action's, as before several actions existed.
    public PickerModel MenuFor(bool startInSettings) =>
        First is null
            ? new PickerModel(PickChoice.Group(_first), _first[0], new Dictionary<string, string>(), startInSettings, shared: _shared)
            : new PickerModel(PickChoice.Group(_first), First.Choice, First.Values, startInSettings, First.Parameter, shared: _shared);

    public void Pick(PickResult pick) => PickSlot(0, pick);

    /// A chip's picker: opens on its action, with its settings beside the list.
    private PickerModel SlotMenu(int index, bool startInSettings)
    {
        var slot = Slots[index];
        return new PickerModel(PickChoice.Group(ListFor(index)), slot.Choice, slot.Values, startInSettings, slot.Parameter, shared: _shared);
    }

    /// The add chip's picker: the one-shot list (or the full list while the input has no action), asking for an action's text first.
    private PickerModel NewMenu() =>
        new(PickChoice.Group(ListFor(Slots.Count)), null, new Dictionary<string, string>(), shared: _shared);

    /// Takes a pick for the action at <paramref name="index"/> (a new last one when it is the count). Default as the first action clears the card.
    private void PickSlot(int index, PickResult pick)
    {
        if (index == 0 && pick.Choice.Spec == MouseActionCatalog.Default)
        {
            _write(Array.Empty<string>());
            Reload();
            return;
        }
        if (index > 0 && !MouseActionCatalog.CanRunLater(MouseActionCatalog.Split(pick.Choice.Spec).Id)) return;
        if (index >= Slots.Count)
        {
            if (Slots.Count >= MouseSettings.MaxActions) return;
            AddChip(new ActionSlot(pick.Choice, _callbacks));
        }
        Slots[index].Take(pick);
        Save();
    }

    private void RemoveSlot(int index)
    {
        if (index < 0 || index >= Slots.Count) return;
        // The next one becomes the first: it keeps its settings, and those only a first action has start at their defaults.
        RemoveChip(index);
        Save();
    }

    /// Moves an action <paramref name="by"/> places (a drag moves it several), when every action lands where it may stand.
    public void MoveSlot(int index, int by)
    {
        var to = Math.Clamp(index + by, 0, Slots.Count - 1);
        if (index < 0 || index >= Slots.Count || to == index) return;
        var order = Slots.ToList();
        var moved = order[index];
        order.RemoveAt(index);
        order.Insert(to, moved);
        if (order.Skip(1).Any(s => !MouseActionCatalog.CanRunLater(s.Id))) return;
        _write(order.Select(s => s.Spec).ToList());
        Reload();
    }

    /// The row's panel left out <paramref name="hidden"/> chips (the "+N more" chip then says so).
    public void ShowFolded(int hidden) => Fold.Show(hidden);

    private void Save()
    {
        _write(Slots.Select(s => s.Spec).ToList());
        Reload();
    }
}
