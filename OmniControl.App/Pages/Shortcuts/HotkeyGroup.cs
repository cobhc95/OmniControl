using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>
/// One folding card of the Shortcuts page at any depth: its own shortcuts (<see cref="Actions"/>), a header summary "N shortcuts · M set"
/// over everything inside it, and "Use suggested keys" when something it offers has a suggestion.
/// </summary>
public abstract class HotkeyNode : CardGroup
{
    private readonly IReadOnlyList<HotkeyAction> _all;

    /// <summary>The shortcuts that sit directly in this card.</summary>
    public IReadOnlyList<HotkeyAction> Actions { get; }

    public bool HasActions => Actions.Count > 0;

    /// <summary>True when an action the pill covers has a suggested key.</summary>
    public bool HasSuggestions { get; }

    /// <summary>Binds the suggested keys (<see cref="HotkeyRegistry.UseSuggested"/>) and says which were skipped.</summary>
    public ICommand UseSuggestedCommand { get; }

    /// <param name="all">Every action inside the card, its inner cards included (what the count is over).</param>
    /// <param name="direct">The actions that sit directly in the card.</param>
    /// <param name="suggested">The actions "Use suggested keys" covers.</param>
    /// <param name="useSuggested">Binds the suggested keys of the actions it is given.</param>
    protected HotkeyNode(string key, string name, IReadOnlyList<HotkeyAction> all, IReadOnlyList<HotkeyAction> direct,
        IReadOnlyList<HotkeyAction> suggested, Action<IReadOnlyList<HotkeyAction>> useSuggested)
        : base(key, name, SummaryOf(all))
    {
        _all = all;
        Actions = direct;
        HasSuggestions = HotkeyDefaults.WithSuggestion(suggested).Any();
        UseSuggestedCommand = new RelayCommand(() => useSuggested(suggested));
    }

    private static string SummaryOf(IReadOnlyList<HotkeyAction> all)
    {
        var set = all.Count(a => a.Gesture.Length > 0);
        var count = all.Count == 1 ? "1 shortcut" : $"{all.Count} shortcuts";
        return set == 0 ? count : $"{count} · {set} set";
    }

    /// <summary>Shows the counts again after a key was given or taken away (here and in the cards inside).</summary>
    public virtual void RefreshCounts() => Summary = SummaryOf(_all);

    /// <summary>The actions in first-seen order of <paramref name="key"/>, one list per key.</summary>
    protected static IEnumerable<IGrouping<string, HotkeyAction>> By(IEnumerable<HotkeyAction> actions, Func<HotkeyAction, string?> key) =>
        actions.Where(a => key(a) is not null).GroupBy(a => key(a)!);
}

/// <summary>
/// The shortcuts of one page of the sidebar (Displays, Volume...) in a folding card: the actions with no group directly, then one inner card
/// per <see cref="HotkeyAction.Group"/>. The master entries say where each belongs; the page's place in the sidebar orders the cards.
/// </summary>
public sealed class HotkeyGroup : HotkeyNode
{
    public IReadOnlyList<HotkeySubgroup> Subgroups { get; }

    public HotkeyGroup(string category, IReadOnlyList<HotkeyAction> actions, Action<IReadOnlyList<HotkeyAction>> useSuggested)
        : this(category, actions, actions.Where(a => a.Group is null && a.Subgroup is null).ToList(), useSuggested) { }

    private HotkeyGroup(string category, IReadOnlyList<HotkeyAction> all, IReadOnlyList<HotkeyAction> direct, Action<IReadOnlyList<HotkeyAction>> useSuggested)
        : base("Shortcuts/" + category, category, all, direct, direct, useSuggested)
    {
        Subgroups = By(all, a => a.Group ?? a.Subgroup)
            .Select(g => new HotkeySubgroup($"Shortcuts/{category}/{g.Key}", g.Key, g.ToList(), useSuggested)).ToList();
    }

    public override void RefreshCounts()
    {
        base.RefreshCounts();
        foreach (var sub in Subgroups) sub.RefreshCounts();
    }
}

/// <summary>One <see cref="HotkeyAction.Group"/> of a page (Hardware, Software, Power): its own shortcuts, then one inner card per <see cref="HotkeyAction.Subgroup"/>.</summary>
public sealed class HotkeySubgroup : HotkeyNode
{
    public IReadOnlyList<HotkeyPart> Parts { get; }

    public HotkeySubgroup(string key, string name, IReadOnlyList<HotkeyAction> all, Action<IReadOnlyList<HotkeyAction>> useSuggested)
        : base(key, name, all, all.Where(a => a.Group is null || a.Subgroup is null).ToList(), all, useSuggested)
    {
        Parts = By(all.Where(a => a.Group is not null), a => a.Subgroup)
            .Select(g => new HotkeyPart($"{key}/{g.Key}", g.Key, g.ToList(), useSuggested)).ToList();
    }

    public override void RefreshCounts()
    {
        base.RefreshCounts();
        foreach (var part in Parts) part.RefreshCounts();
    }
}

/// <summary>One <see cref="HotkeyAction.Subgroup"/> inside a group (Colour, Presets): its shortcuts.</summary>
public sealed class HotkeyPart : HotkeyNode
{
    public HotkeyPart(string key, string name, IReadOnlyList<HotkeyAction> all, Action<IReadOnlyList<HotkeyAction>> useSuggested)
        : base(key, name, all, all, all, useSuggested) { }
}
