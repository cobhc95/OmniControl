using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// A row's "Method: Automatic" button and its pop-down: one card per way its effects can run, from a master list of
/// <see cref="EffectsRoute"/>s (a program's <see cref="EffectsRules.ProgramRoutes"/>, the master's <see cref="EffectsRules.MasterRoutes"/>).
/// Picking a card reports it through <c>chosen</c>; the page stores it and calls <see cref="Sync"/>. A card that cannot work on this PC
/// right now is disabled and says why (<see cref="EffectsRules.OfferOf"/>, <see cref="EffectsRules.MasterOfferOf"/>); the one step that
/// fixes what is missing can sit under the cards (<see cref="Fix"/>).
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

    /// A program's method, with the Use Recording step for a program left without a cable.
    /// <param name="refresh">Reads the cables and the engine again as the pop-down opens (<see cref="Refresh"/>).</param>
    public static RouteChoice ForProgram(Action<EffectsRoute> chosen, Action refresh) =>
        new("How the effects run", "How this program's effects run.", EffectsRules.ProgramRoutes, chosen)
        {
            Refresh = refresh,
            UseRecording = new CardAction(EffectsRules.UseRecordingLabel, EffectsRules.UseRecordingTip, new RelayCommand(() => chosen(EffectsRules.RouteCapture))),
        };

    /// The master's method: the cable bus or Equalizer APO on the speakers.
    public static RouteChoice ForMaster(Action<EffectsRoute> chosen, Action refresh) =>
        new("How the master runs", "How the master runs.", EffectsRules.MasterRoutes, chosen)
        {
            Refresh = refresh,
            UseApo = new CardAction(EffectsRules.UseApoLabel, EffectsRules.UseApoTip, new RelayCommand(() => chosen(EffectsRules.MasterApo))),
        };

    public string Title { get; }

    /// Asks the page to read the cables and the engine again; called as the pop-down opens, so it never shows a stale state.
    public Action? Refresh { get; private init; }

    /// A program's one step when it is set to Virtual cable and has no cable: use Recording (null on the master).
    public CardAction? UseRecording { get; private init; }

    /// The master's one step when it is set to the cable bus and has no cable: use Equalizer APO (null on a program).
    public CardAction? UseApo { get; private init; }

    /// Recording can be chosen now (the step above is only offered then).
    public bool RecordingAvailable => Cards.Any(c => c.Route == EffectsRules.RouteCapture && c.IsAvailable);

    /// Equalizer APO on the speakers can be chosen now (<see cref="UseApo"/> is only offered then).
    public bool ApoAvailable => Cards.Any(c => c.Route == EffectsRules.MasterApo && c.IsAvailable);

    public IReadOnlyList<RouteCard> Cards { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ButtonText), nameof(Tip))] private EffectsRoute _current;

    /// The step under the cards that fixes what a disabled card lacks (the master's Equalizer APO: Choose devices); null = none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] private CardAction? _fix;
    public bool HasFix => Fix is not null;

    /// The install and remove steps under the fix step (the master's menu only: Install Equalizer APO, Choose devices, Install VB-CABLE
    /// or More cables, Remove VB-CABLE); empty = none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSteps))] private IReadOnlyList<CardAction> _steps = Array.Empty<CardAction>();
    public bool HasSteps => Steps.Count > 0;

    public string ButtonText => $"Method: {Current.Name}";
    public string Tip => $"{_tipLead} {Current.Tip}";

    /// Shows the stored entry, each card's live state and the fix step.
    public void Sync(EffectsRoute current, Func<EffectsRoute, RouteOffer> offer, CardAction? fix = null)
    {
        Current = current;
        foreach (var card in Cards) card.Sync(card.Route == current, offer(card.Route));
        if (fix?.Label != Fix?.Label) Fix = fix;
    }

    /// The steps as they stand now, without the fix step's own (kept when their words are the same, so a button that has the keyboard keeps it).
    public void SyncSteps(IReadOnlyList<CardAction> steps)
    {
        var shown = steps.Where(s => s.Label != Fix?.Label).ToList();
        if (!shown.Select(a => a.Label).SequenceEqual(Steps.Select(a => a.Label))) Steps = shown;
    }

    /// A program's menu from its facts (<see cref="EffectsRules.OfferOf"/>) and the step that fixes what a greyed card lacks.
    public void SyncProgram(EffectsRoute current, RouteFacts facts, CardAction? fix = null) => Sync(current, r => EffectsRules.OfferOf(r, facts), fix);
}

/// One method's card in a Method pop-down.
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

    /// False = it cannot work on this PC now: greyed out, its description and tooltip say why.
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
