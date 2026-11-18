using OmniControl.App.Hotkeys;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>What one shown slider, tile or menu item is: the element it comes from and the live action and/or state it found.</summary>
public sealed record TrayTarget(TrayElement Element, HotkeyAction? Action, StateEntry? State, StateEntry? Choice = null, IReadOnlyList<StateEntry>? Group = null, StateEntry? AllLevel = null)
{
    /// <summary>The words it shows: the action's name (a state that only ticks or lights it never renames it), else a state alone's short name.</summary>
    public string Name => Action?.ShortName ?? State?.ShortName ?? string.Empty;

    /// <summary>The section whose icon it shows unless the element names another: the state's page, else the action's section.</summary>
    public string Section => Element.Icon ?? State?.Group ?? Action?.Section ?? string.Empty;

    /// <summary>The icon the entry brings itself (its action's, else its state's), unless the element names one to show instead; null: the section's icon.</summary>
    public string? OwnIcon => Element.Icon is null ? Action?.ShownIcon ?? State?.ShownIcon : null;
}

/// <summary>
/// Turns stored elements into what is shown, read from the live master lists each time (pure: the lists are passed in, so a test gives its
/// own). An id with a <c>*</c> gives one target per match, except an id the same list names on its own (<see cref="TrayOrder"/> spells a
/// pattern out that way); an id that matches nothing, or a state of the wrong kind (a slider needs a level, a tile or a tick an on/off), gives none.
/// </summary>
public static class TrayTargets
{
    /// <summary>Sliders: level states, each with its pick list when the element names a choice state.</summary>
    public static IReadOnlyList<TrayTarget> Sliders(IEnumerable<TrayElement> elements, IReadOnlyList<StateEntry> states)
    {
        var list = elements.ToList();
        var named = Named(list, e => e.State);
        return list.SelectMany(e => e.State is { } id ? SliderTargets(e, id, states, named) : Enumerable.Empty<TrayTarget>()).ToList();
    }

    /// <summary>One target per matching level state, or, for a together element, one target holding every match (the first stands for its name and icon).</summary>
    private static IEnumerable<TrayTarget> SliderTargets(TrayElement element, string id, IReadOnlyList<StateEntry> states, IReadOnlySet<string> named)
    {
        var matches = StatesOf(id, states, StateKind.Level, named).ToList();
        var choice = ChoiceOf(element, states);
        var all = element.AllState is { } allId ? states.FirstOrDefault(s => s.Kind == StateKind.Level && s.Id == allId) : null;
        if (!element.Together || !IdPattern.IsPattern(id)) return matches.Select(s => new TrayTarget(element, null, s, choice, AllLevel: all));
        return matches.Count == 0 ? Enumerable.Empty<TrayTarget>() : new[] { new TrayTarget(element, null, matches[0], choice, matches) };
    }

    /// <summary>Tiles and menu items: an action (lit or ticked by its state when it names an on/off one), or an on/off state alone.</summary>
    /// <param name="findAction">The master list's lookup by id (it follows renamed ids); patterns are matched over <paramref name="actions"/>.</param>
    public static IReadOnlyList<TrayTarget> Toggles(IEnumerable<TrayElement> elements, IReadOnlyList<StateEntry> states,
        IEnumerable<HotkeyAction> actions, Func<string, HotkeyAction?> findAction)
    {
        var list = elements.ToList();
        var (namedActions, namedStates) = (Named(list.Where(e => e.Action is not null), e => e.Action), Named(list.Where(e => e.Action is null), e => e.State));
        return list.SelectMany(e => e.Action is { } id
            ? ActionsOf(id, actions, findAction, namedActions).Select(a => new TrayTarget(e, a, e.State is { } s ? StatesOf(s, states, StateKind.OnOff, NoNames).FirstOrDefault() : null))
            : e.State is { } state
                ? StatesOf(state, states, StateKind.OnOff, namedStates).Select(s => new TrayTarget(e, null, s))
                : Enumerable.Empty<TrayTarget>()).ToList();
    }

    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    /// <summary>The ids the elements name on their own (no <c>*</c>): a pattern in the same list leaves them out.</summary>
    private static IReadOnlySet<string> Named(IEnumerable<TrayElement> elements, Func<TrayElement, string?> idOf) =>
        elements.Select(idOf).OfType<string>().Where(id => !IdPattern.IsPattern(id)).ToHashSet();

    private static IEnumerable<StateEntry> StatesOf(string id, IReadOnlyList<StateEntry> states, StateKind kind, IReadOnlySet<string> named) =>
        states.Where(s => s.Kind == kind && IdPattern.Matches(id, s.Id) && (!IdPattern.IsPattern(id) || !named.Contains(s.Id)));

    private static IEnumerable<HotkeyAction> ActionsOf(string id, IEnumerable<HotkeyAction> actions, Func<string, HotkeyAction?> findAction, IReadOnlySet<string> named) =>
        IdPattern.IsPattern(id) ? actions.Where(a => IdPattern.Matches(id, a.Id) && !named.Contains(a.Id))
            : findAction(id) is { } action ? new[] { action } : Enumerable.Empty<HotkeyAction>();

    private static StateEntry? ChoiceOf(TrayElement element, IReadOnlyList<StateEntry> states) =>
        element.Choice is { } id ? states.FirstOrDefault(s => s.Kind == StateKind.Choice && s.Id == id) : null;
}
