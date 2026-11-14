using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>A button on a sound mode card (Install Equalizer APO, Choose devices, Install VB-CABLE...); <c>Main</c> marks the one that does the job.</summary>
public sealed record ModeCardAction(string Label, string Tip, IRelayCommand Command, bool Main = false);

/// <summary>
/// The page's "Sound mode: Fast (8)" button (on both tabs, one view-model) and its panel: one card per mode of the <see cref="SoundModes"/>
/// master list, each with its plain status on this PC and its setup buttons. Picking a card reports the mode through <c>chosen</c>; the
/// page stores it and calls <see cref="Sync"/>. The badge shows the live setup number (<see cref="SoundModes.SetupNumber"/>); clicking
/// it opens the setup table (<see cref="ShowTableCommand"/>).
/// </summary>
public sealed partial class SoundModeChoice : ObservableObject
{
    private readonly Action? _opened;
    private readonly Action _showTable;

    /// <param name="chosen">The user picked a mode.</param>
    /// <param name="opened">The panel opened (a moment to look at what this PC offers again).</param>
    /// <param name="showTable">Opens the setup table (the badge, the Help button).</param>
    public SoundModeChoice(Action<SoundMode> chosen, Action? opened, Action showTable)
    {
        _opened = opened;
        _showTable = showTable;
        Cards = SoundModes.All.Select(m => new SoundModeCard(m, chosen)).ToList();
        _current = SoundModes.Default;
    }

    /// <summary>The live setup's number on the badge, and its one-line tooltip.</summary>
    [ObservableProperty] private int _setup = SoundModes.FastSetup;
    [ObservableProperty] private string _setupTip = SoundModes.SetupTip(SoundModes.FastSetup);

    [RelayCommand] private void ShowTable() => _showTable();

    public IReadOnlyList<SoundModeCard> Cards { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ButtonText), nameof(Tip))] private SoundMode _current;

    /// <summary>The panel is open: the button is tinted in the accent.</summary>
    [ObservableProperty] private bool _isOpen;

    public string ButtonText => $"Sound mode: {Current.Name}";
    public string Tip => Current.Description;

    /// <summary>One line under the cards: the switch applies to every row, effects run only while OmniControl is open.</summary>
    public string Footnote => "Applies to every row. Effects run only while OmniControl is open.";

    partial void OnIsOpenChanged(bool value) { if (value) _opened?.Invoke(); }

    /// <summary>Shows the stored mode, each mode's status and each card's buttons.</summary>
    /// <param name="unavailable">Why a mode can't be chosen now (null = it can): its card is dimmed with that reason and its buttons keep working.</param>
    public void Sync(SoundMode current, Func<SoundMode, IReadOnlyList<ModeStatus>> status, Func<SoundMode, IReadOnlyList<ModeCardAction>> actions,
        Func<SoundMode, string?> unavailable)
    {
        Current = current;
        foreach (var card in Cards) card.Sync(card.Mode == current, status(card.Mode), actions(card.Mode), unavailable(card.Mode));
    }

    /// <summary>The badge follows the live setup.</summary>
    public void SyncSetup(int number)
    {
        Setup = number;
        SetupTip = SoundModes.SetupTip(number);
    }

    /// <summary>A line on one card (a setup that is running or has ended).</summary>
    public void SetNote(SoundMode mode, string note)
    {
        foreach (var card in Cards.Where(c => c.Mode == mode)) card.Note = note;
    }
}

/// <summary>One mode's card in the Sound mode panel.</summary>
public sealed partial class SoundModeCard : ObservableObject
{
    private readonly Action<SoundMode> _chosen;

    public SoundModeCard(SoundMode mode, Action<SoundMode> chosen)
    {
        Mode = mode;
        _chosen = chosen;
    }

    public SoundMode Mode { get; }
    public string Name => Mode.Name;
    public string Description => Mode.Description;
    /// <summary>The card's tooltip: why it can't be chosen now (when so), how effects run in this mode, then the technical details.</summary>
    public string Tip => (UnavailableReason is null ? string.Empty : $"{UnavailableReason}\n") + $"{Mode.MethodSummary}\n{Mode.Details}";
    public IReadOnlyList<ModeTag> Tags => Mode.Tags;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Why this mode can't be chosen on this PC now; null = it can. The card is dimmed and does nothing when picked; its buttons (Install VB-CABLE...) still work.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUnavailable), nameof(Tip))] private string? _unavailableReason;

    public bool IsUnavailable => UnavailableReason is not null;
    [ObservableProperty] private IReadOnlyList<ModeStatus> _statuses = Array.Empty<ModeStatus>();
    [ObservableProperty] private IReadOnlyList<ModeCardAction> _actions = Array.Empty<ModeCardAction>();

    /// <summary>A result or progress line under the description (empty = none).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    public void Sync(bool selected, IReadOnlyList<ModeStatus> statuses, IReadOnlyList<ModeCardAction> actions, string? unavailableReason)
    {
        IsSelected = selected;
        UnavailableReason = unavailableReason;
        Statuses = statuses;
        if (!actions.Select(a => a.Label).SequenceEqual(Actions.Select(a => a.Label))) Actions = actions;
    }

    [RelayCommand] private void Pick()
    {
        if (!IsUnavailable) _chosen(Mode);
    }
}
