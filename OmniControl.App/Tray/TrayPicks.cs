using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>
/// The lists the flyout's "+ slider" and "+ tile" open in the Choose-the-action window, read from the live master lists each time:
/// every level state (a slider), every master action and every on/off state (a tile). A state sits in its page's section, beside that
/// page's actions. The answer becomes a <see cref="TrayElement"/> by id.
/// </summary>
internal static class TrayPicks
{
    private const string StatePrefix = "state:";
    private const string StatesHeading = "States";
    private const string ReadOnlyWords = "It can only be read, not changed from here.";
    private const string SliderNoun = "level", TileNoun = "tile";

    public static (string Title, PickerModel Model) ForSlider(IReadOnlyList<StateEntry> states, Func<string, ActionSection?> sectionOf) =>
        ("Choose a level for a slider", Model(states.Where(s => s.Kind == StateKind.Level).Select(s => StateChoice(s, sectionOf, needsSet: true)), SliderNoun));

    public static (string Title, PickerModel Model) ForTile(IReadOnlyList<StateEntry> states, IEnumerable<HotkeyAction> actions, Func<string, ActionSection?> sectionOf) =>
        ("Choose an action or an on/off for a tile", Model(
            actions.Select(a => MouseActionCatalog.OmniEntry(a, unavailable: a.UnavailableReason))
                .Concat(states.Where(s => s.Kind == StateKind.OnOff).Select(s => StateChoice(s, sectionOf, needsSet: false))), TileNoun));

    /// <summary>The element a pick stands for; null for an entry that is neither a master action nor a state.</summary>
    public static TrayElement? ElementOf(PickResult pick) =>
        pick.Choice.Spec.StartsWith(StatePrefix, StringComparison.Ordinal) ? TrayElement.OfState(pick.Choice.Spec[StatePrefix.Length..])
        : MouseActionCatalog.OmniIdOf(pick.Choice.Spec) is { } id ? TrayElement.OfAction(id)
        : null;

    private static PickerModel Model(IEnumerable<PickChoice> choices, string noun) =>
        new(PickChoice.Group(choices), null, new Dictionary<string, string>()) { Noun = noun };

    /// <param name="needsSet">A slider must be able to change its level; a tile may only show a state.</param>
    private static PickChoice StateChoice(StateEntry state, Func<string, ActionSection?> sectionOf, bool needsSet) =>
        new($"{StatePrefix}{state.Id}", state.Group, state.Name, Keywords: $"state {state.Keywords}", Heading: state.Heading ?? StatesHeading,
            Place: sectionOf(state.Group), Unavailable: needsSet && state.Set is null ? ReadOnlyWords : null);
}
