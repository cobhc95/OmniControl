using OmniControl.App.AppLists;
using OmniControl.App.Hotkeys;
using System.Collections.ObjectModel;
using OmniControl.App.Picker;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Pages.Mouse;

/// <summary>What a card's Windows tag says: nothing, Windows also runs its own action (asked for), or turning Windows' own one off failed.</summary>
public enum WindowsTag { None, AlsoRuns, Problem }

/// <summary>
/// One input's half-width card (a mouse button, or a trackpad gesture): its actions in running order (<see cref="Slots"/>, shown as
/// chips), each with its settings and text (a shortcut or a program path). The first action keeps every setting it has today (when it
/// runs, letting the input through); the second and later ones are one-shot actions (<see cref="MouseActionCatalog.ExtraChoices"/>)
/// that run after it. The card owns the list and stores it whole after every change. A trackpad gesture's card can be unavailable
/// (no precision touchpad) and says when Windows also acts on it.
/// </summary>
public sealed partial class MouseButtonItem : ObservableObject, IEditorItem, IHalfWidth, IPickerCard
{
    private readonly Func<IReadOnlyList<PickChoice>> _firstSource, _laterSource;
    private IReadOnlyList<PickChoice> _first, _later;
    private readonly Func<IReadOnlyList<string>> _read;
    private readonly Action<IReadOnlyList<string>> _write;
    private readonly ActionSlot.Callbacks _callbacks;

    public MouseButton Button { get; }

    public string Label { get; }

    /// <summary>True for a moment after the real button is pressed, so the card lights up.</summary>
    [ObservableProperty] private bool _isFlashing;

    private int _flashId;

    /// <summary>Lights the card for <see cref="Timing.MouseFlash"/>; a press during the flash restarts it.</summary>
    public async void Flash()
    {
        var mine = ++_flashId;
        IsFlashing = true;
        await Task.Delay(Timing.MouseFlash);
        if (mine == _flashId) IsFlashing = false;
    }

    /// <summary>Two buttons share a row.</summary>
    public bool Half => true;

    /// <summary>The actions in the order they run; empty while the input is at Default.</summary>
    public ObservableCollection<ActionSlot> Slots { get; } = new();

    /// <summary>What the chip row shows: every action's chip, then the "+N more" chip and the dashed add chip (the row's panel folds the rest).</summary>
    public ObservableCollection<object> Chips { get; } = new();

    public FoldChip Fold { get; } = new();
    public AddChip Add { get; }

    /// <summary>Greyed out with the reason when the input can't be seen now (a trackpad gesture without a precision touchpad).</summary>
    public Availability Availability { get; } = new();

    /// <summary>What Windows itself does with this input (a trackpad gesture Windows' touchpad settings use); the page keeps it current.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWindowsAlso), nameof(ShowsWindowsProblem))]
    private WindowsTag _windowsState;

    /// <summary>The tag's tooltip: what Windows does, or why its own gesture could not be turned off.</summary>
    [ObservableProperty] private string _windowsDetail = string.Empty;

    // The card's tags show only once the input has an action of its own, since at "Default" Windows doing its own thing is what is wanted.
    public bool ShowsWindowsAlso => WindowsState == WindowsTag.AlsoRuns && IsCustom;
    public bool ShowsWindowsProblem => WindowsState == WindowsTag.Problem && IsCustom;

    /// <summary>True when the first action's "also keep the original" setting is ticked (for a trackpad gesture: Windows' own gesture stays on).</summary>
    public bool KeepsOriginal => First is { } first && new OptionValues(first.Values).Bool(MouseOptions.KeepOriginal);

    public const string WindowsAlsoText = "Windows also does this";
    public const string WindowsProblemText = "Windows' own still on";

    /// <summary>What the pill says while the input has no action of its own.</summary>
    public const string Summary = "Default";

    private ActionSlot? First => Slots.Count > 0 ? Slots[0] : null;

    /// <summary>True when the input has been given an action (its chips show); false while it behaves as Windows normally does.</summary>
    public bool IsCustom => Slots.Count > 0;

    /// <summary>The text the first action takes (a shortcut, a program), edited in the box under the card.</summary>
    public string Parameter
    {
        get => First?.Parameter ?? string.Empty;
        set
        {
            if (First is not { } first || first.Parameter == value) return;
            first.Parameter = value;
            Save();
        }
    }

    /// <summary>The first action's text is a plain one (a shortcut): a text box.</summary>
    public bool NeedsParameter => First?.Choice is { ParamHint: not null, ParamBrowse: null };

    /// <summary>The first action's text is a path (a program, a file, a folder): the shared path field with Browse.</summary>
    public bool NeedsPath => First?.Choice is { ParamHint: not null, ParamBrowse: not null };
    public PathKind PathKind => First?.Choice.ParamBrowse ?? PathKind.File;
    public string Hint => First?.Choice.ParamHint ?? string.Empty;

    /// <summary>True when the first action has settings.</summary>
    public bool HasOptions => First?.HasOptions == true;

    /// <summary>The card's own apps scope (its cogwheel's menu); null for a trackpad gesture, which has none.</summary>
    public AppScopeModel? Scope { get; }

    /// <summary>The card shows a cogwheel: its action has settings, or it can be limited to apps.</summary>
    public bool ShowsCog => HasOptions || Scope is not null;

    /// <summary>The tag under the card: its apps, shown only while they differ from the group's.</summary>
    public bool ShowsScopeTag => Scope?.ShowsTag == true;

    public string ScopeTagText => Scope?.Text ?? string.Empty;

    /// <summary>Every action's name, so search finds the card by any of them.</summary>
    public string SearchWords => string.Join(" ", Slots.Select(s => s.Name));

    /// <param name="firstChoices">The first action's list as it is now (it grows as features register OmniControl actions).</param>
    /// <param name="laterChoices">The list for the second and later actions (one-shot only).</param>
    /// <param name="read">The stored actions of this input right now, in order.</param>
    /// <param name="write">Stores the actions in order.</param>
    /// <param name="scope">The card's apps scope, or null when the card has none.</param>
    public MouseButtonItem(MouseButton button, string label, Func<IReadOnlyList<PickChoice>> firstChoices, Func<IReadOnlyList<PickChoice>> laterChoices,
        Func<IReadOnlyList<string>> read, Action<IReadOnlyList<string>> write, AppScopeModel? scope = null)
    {
        Scope = scope;
        if (scope is not null) scope.PropertyChanged += (_, _) =>
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

    /// <summary>Shows what is stored now (after the layer or the action list changed), without writing anything back.</summary>
    public void Reload()
    {
        _first = _firstSource();
        _later = _laterSource();
        Scope?.Refresh();
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

    /// <summary>Numbers, labels, which way each chip may move, the add chip, and the card's first-action properties.</summary>
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
        OnPropertyChanged(nameof(NeedsParameter));
        OnPropertyChanged(nameof(NeedsPath));
        OnPropertyChanged(nameof(PathKind));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(ShowsCog));
        OnPropertyChanged(nameof(ShowsWindowsAlso));
        OnPropertyChanged(nameof(ShowsWindowsProblem));
    }

    /// <summary>
    /// True when the action at <paramref name="from"/> may swap places with the one at <paramref name="to"/>: the second and later places
    /// take only one-shot actions (<see cref="MouseActionCatalog.CanRunLater"/>).
    /// </summary>
    private bool MayPlace(int from, int to) =>
        (to == 0 || MouseActionCatalog.CanRunLater(Slots[from].Id)) && (from == 0 || MouseActionCatalog.CanRunLater(Slots[to].Id));

    /// <summary>The card's own picker (the Default pill, Enter on the card, the cogwheel): the first action's, as before several actions existed.</summary>
    public PickerModel MenuFor(bool startInSettings) =>
        First is null
            ? new PickerModel(PickChoice.Group(_first), _first[0], new Dictionary<string, string>(), startInSettings)
            : new PickerModel(PickChoice.Group(_first), First.Choice, First.Values, startInSettings, First.Parameter);

    public void Pick(PickResult pick) => PickSlot(0, pick);

    /// <summary>A chip's picker: opens on its action, with its settings beside the list.</summary>
    private PickerModel SlotMenu(int index, bool startInSettings)
    {
        var slot = Slots[index];
        return new PickerModel(PickChoice.Group(ListFor(index)), slot.Choice, slot.Values, startInSettings, slot.Parameter);
    }

    /// <summary>The add chip's picker: the one-shot list (or the full list while the input has no action), asking for an action's text first.</summary>
    private PickerModel NewMenu() =>
        new(PickChoice.Group(ListFor(Slots.Count)), null, new Dictionary<string, string>());

    /// <summary>Takes a pick for the action at <paramref name="index"/> (a new last one when it is the count). Default as the first action clears the card.</summary>
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

    /// <summary>Moves an action <paramref name="by"/> places (a drag moves it several), when every action lands where it may stand.</summary>
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

    /// <summary>The row's panel left out <paramref name="hidden"/> chips (the "+N more" chip then says so).</summary>
    public void ShowFolded(int hidden) => Fold.Show(hidden);

    private void Save()
    {
        _write(Slots.Select(s => s.Spec).ToList());
        Reload();
    }
}
