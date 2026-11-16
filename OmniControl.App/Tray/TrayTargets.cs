using OmniControl.App.Hotkeys;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>What one shown slider, tile or menu item is: the element it comes from and the live action and/or state it found.</summary>
public sealed record TrayTarget(TrayElement Element, HotkeyAction? Action, StateEntry? State, StateEntry? Choice = null)
{
    /// <summary>The words it shows: the action's name (a state that only ticks or lights it never renames it), else a state alone's short name.</summary>
    public string Name => Action?.ShortName ?? State?.ShortName ?? string.Empty;

    /// <summary>The section whose icon it shows unless the element names another: the state's page, else the action's section.</summary>
    public string Section => Element.Icon ?? State?.Group ?? Action?.Section ?? string.Empty;
}

/// <summary>
/// Turns stored elements into what is shown, read from the live master lists each time (pure: the lists are passed in, so a test gives its
/// own). An id with a <c>*</c> gives one target per match; an id that matches nothing, or a state of the wrong kind (a slider needs a level,
/// a tile or a tick an on/off), gives none.
/// </summary>
public static class TrayTargets
{
    /// <summary>Sliders: level states, each with its pick list when the element names a choice state.</summary>
    public static IReadOnlyList<TrayTarget> Sliders(IEnumerable<TrayElement> elements, IReadOnlyList<StateEntry> states) =>
        elements.SelectMany(e => e.State is { } id
            ? StatesOf(id, states, StateKind.Level).Select(s => new TrayTarget(e, null, s, ChoiceOf(e, states)))
            : Enumerable.Empty<TrayTarget>()).ToList();

    /// <summary>Tiles and menu items: an action (lit or ticked by its state when it names an on/off one), or an on/off state alone.</summary>
    /// <param name="findAction">The master list's lookup by id (it follows renamed ids); patterns are matched over <paramref name="actions"/>.</param>
    public static IReadOnlyList<TrayTarget> Toggles(IEnumerable<TrayElement> elements, IReadOnlyList<StateEntry> states,
        IEnumerable<HotkeyAction> actions, Func<string, HotkeyAction?> findAction) =>
        elements.SelectMany(e => e.Action is { } id
            ? ActionsOf(id, actions, findAction).Select(a => new TrayTarget(e, a, e.State is { } s ? StatesOf(s, states, StateKind.OnOff).FirstOrDefault() : null))
            : e.State is { } state
                ? StatesOf(state, states, StateKind.OnOff).Select(s => new TrayTarget(e, null, s))
                : Enumerable.Empty<TrayTarget>()).ToList();

    private static IEnumerable<StateEntry> StatesOf(string id, IReadOnlyList<StateEntry> states, StateKind kind) =>
        states.Where(s => s.Kind == kind && IdPattern.Matches(id, s.Id));

    private static IEnumerable<HotkeyAction> ActionsOf(string id, IEnumerable<HotkeyAction> actions, Func<string, HotkeyAction?> findAction) =>
        IdPattern.IsPattern(id) ? actions.Where(a => IdPattern.Matches(id, a.Id)) : findAction(id) is { } action ? new[] { action } : Enumerable.Empty<HotkeyAction>();

    private static StateEntry? ChoiceOf(TrayElement element, IReadOnlyList<StateEntry> states) =>
        element.Choice is { } id ? states.FirstOrDefault(s => s.Kind == StateKind.Choice && s.Id == id) : null;
}
