using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>A button on a sound mode card (Install Equalizer APO, Choose devices, Install VB-CABLE...); <c>Main</c> marks the one that does the job.</summary>
public sealed record ModeCardAction(string Label, string Tip, IRelayCommand Command, bool Main = false);

/// <summary>
/// The page's "Sound mode: Fast" button and its panel: one card per mode of the <see cref="SoundModes"/> master list, each with its
/// plain status on this PC and its setup buttons. Picking a card reports the mode through <c>chosen</c>; the page stores it and calls
/// <see cref="Sync"/>.
/// </summary>
public sealed partial class SoundModeChoice : ObservableObject
{
    private readonly Action? _opened;

    /// <param name="chosen">The user picked a mode.</param>
    /// <param name="opened">The panel opened (a moment to look at what this PC offers again).</param>
    public SoundModeChoice(Action<SoundMode> chosen, Action? opened)
    {
        _opened = opened;
        Cards = SoundModes.All.Select(m => new SoundModeCard(m, chosen)).ToList();
        _current = SoundModes.Default;
    }

    public IReadOnlyList<SoundModeCard> Cards { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ButtonText), nameof(Tip))] private SoundMode _current;

    /// <summary>The panel is open: the button is tinted in the accent.</summary>
    [ObservableProperty] private bool _isOpen;

    public string ButtonText => $"Sound mode: {Current.Name}";
    public string Tip => Current.Description;

    /// <summary>One line under the cards: the switch applies to every row, effects run only while OmniControl is open.</summary>
    public string Footnote => "Applies to every row at once. Effects run only while OmniControl is open; without them the sound plays unprocessed.";

    partial void OnIsOpenChanged(bool value) { if (value) _opened?.Invoke(); }

    /// <summary>Shows the stored mode, each mode's status and each card's buttons.</summary>
    public void Sync(SoundMode current, Func<SoundMode, IReadOnlyList<ModeStatus>> status, Func<SoundMode, IReadOnlyList<ModeCardAction>> actions)
    {
        Current = current;
        foreach (var card in Cards) card.Sync(card.Mode == current, status(card.Mode), actions(card.Mode));
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
    public string Details => Mode.Details;
    public string MethodSummary => Mode.MethodSummary;
    public IReadOnlyList<ModeTag> Tags => Mode.Tags;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private IReadOnlyList<ModeStatus> _statuses = Array.Empty<ModeStatus>();
    [ObservableProperty] private IReadOnlyList<ModeCardAction> _actions = Array.Empty<ModeCardAction>();

    /// <summary>A result or progress line under the description (empty = none).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    public void Sync(bool selected, IReadOnlyList<ModeStatus> statuses, IReadOnlyList<ModeCardAction> actions)
    {
        IsSelected = selected;
        Statuses = statuses;
        if (!actions.Select(a => a.Label).SequenceEqual(Actions.Select(a => a.Label))) Actions = actions;
    }

    [RelayCommand] private void Pick() => _chosen(Mode);
}
