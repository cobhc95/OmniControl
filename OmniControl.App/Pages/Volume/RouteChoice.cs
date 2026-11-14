using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A row's "Method: Automatic" button and its pop-down: one card per way its effects can run, from a master list of
/// <see cref="EffectsRoute"/>s (a program's <see cref="SoundModes.ProgramRoutes"/>, the master's <see cref="SoundModes.MasterRoutes"/>).
/// Picking a card reports it through <c>chosen</c>; the page stores it and calls <see cref="Sync"/>. A card that cannot work on this PC
/// right now is disabled and says why (<see cref="SoundModes.OfferOf"/>, <see cref="SoundModes.MasterOfferOf"/>); the one step that
/// fixes what is missing can sit under the cards (<see cref="Fix"/>).
/// </summary>
public sealed partial class RouteChoice : ObservableObject
{
    private readonly string _tipLead;

    /// <param name="title">The pop-down's title.</param>
    /// <param name="tipLead">The button's tooltip before the current entry's own.</param>
    private RouteChoice(string title, string tipLead, IReadOnlyList<EffectsRoute> routes, Action<EffectsRoute> chosen)
    {
        Title = title;
        _tipLead = tipLead;
        Cards = routes.Select(r => new RouteCard(r, chosen)).ToList();
        _current = routes[0];
    }

    /// <summary>A program's method (Full control), with the Use Recording step for a program left without a cable.</summary>
    /// <param name="refresh">Reads the cables and the engine again as the pop-down opens (<see cref="Refresh"/>).</param>
    public static RouteChoice ForProgram(Action<EffectsRoute> chosen, Action refresh) =>
        new("How the effects run", "How this program's effects run in Full control.", SoundModes.ProgramRoutes, chosen)
        {
            Refresh = refresh,
            UseRecording = new ModeCardAction(SoundModes.UseRecordingLabel, SoundModes.UseRecordingTip, new RelayCommand(() => chosen(SoundModes.RouteCapture))),
        };

    /// <summary>The master's method (Full control): the cable bus or Equalizer APO on the speakers.</summary>
    public static RouteChoice ForMaster(Action<EffectsRoute> chosen, Action refresh) =>
        new("How the master runs", "How the master runs in Full control.", SoundModes.MasterRoutes, chosen)
        {
            Refresh = refresh,
            UseApo = new ModeCardAction(SoundModes.UseApoLabel, SoundModes.UseApoTip, new RelayCommand(() => chosen(SoundModes.MasterApo))),
        };

    public string Title { get; }

    /// <summary>Asks the page to read the cables and the engine again; called as the pop-down opens, so it never shows a stale state.</summary>
    public Action? Refresh { get; private init; }

    /// <summary>A program's one step when it is set to Virtual cable and has no cable: use Recording (null on the master).</summary>
    public ModeCardAction? UseRecording { get; private init; }

    /// <summary>The master's one step when it is set to the cable bus and has no cable: use Equalizer APO (null on a program).</summary>
    public ModeCardAction? UseApo { get; private init; }

    /// <summary>Recording can be chosen now (the step above is only offered then).</summary>
    public bool RecordingAvailable => Cards.Any(c => c.Route == SoundModes.RouteCapture && c.IsAvailable);

    /// <summary>Equalizer APO on the speakers can be chosen now (<see cref="UseApo"/> is only offered then).</summary>
    public bool ApoAvailable => Cards.Any(c => c.Route == SoundModes.MasterApo && c.IsAvailable);

    public IReadOnlyList<RouteCard> Cards { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ButtonText), nameof(Tip))] private EffectsRoute _current;

    /// <summary>The step under the cards that fixes what a disabled card lacks (the master's Equalizer APO: Choose devices); null = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] private ModeCardAction? _fix;
    public bool HasFix => Fix is not null;

    public string ButtonText => $"Method: {Current.Name}";
    public string Tip => $"{_tipLead} {Current.Tip}";

    /// <summary>Shows the stored entry, each card's live state and the fix step.</summary>
    public void Sync(EffectsRoute current, Func<EffectsRoute, RouteOffer> offer, ModeCardAction? fix = null)
    {
        Current = current;
        foreach (var card in Cards) card.Sync(card.Route == current, offer(card.Route));
        if (fix?.Label != Fix?.Label) Fix = fix;
    }

    /// <summary>A program's menu from its facts (<see cref="SoundModes.OfferOf"/>) and the step that fixes what a greyed card lacks.</summary>
    public void SyncProgram(EffectsRoute current, RouteFacts facts, ModeCardAction? fix = null) => Sync(current, r => SoundModes.OfferOf(r, facts), fix);
}

/// <summary>One method's card in a Method pop-down.</summary>
public sealed partial class RouteCard : ObservableObject
{
    private readonly Action<EffectsRoute> _chosen;

    public RouteCard(EffectsRoute route, Action<EffectsRoute> chosen)
    {
        Route = route;
        _chosen = chosen;
        _description = route.Description;
        _tip = route.Tip;
    }

    public EffectsRoute Route { get; }
    public string Name => Route.Name;

    [ObservableProperty] private string _description;
    [ObservableProperty] private string _tip;
    [ObservableProperty] private bool _isSelected;

    /// <summary>False = it cannot work on this PC now: greyed out, its description and tooltip say why.</summary>
    [ObservableProperty] private bool _isAvailable = true;

    public void Sync(bool selected, RouteOffer offer)
    {
        IsSelected = selected;
        IsAvailable = offer.Available;
        Description = offer.Description;
        Tip = offer.Tip;
    }

    [RelayCommand] private void Pick()
    {
        if (IsAvailable) _chosen(Route);
    }
}
