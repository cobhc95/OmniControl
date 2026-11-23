using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Tray;

/// The lists the flyout's "Add a slider…" and "Add a tile…" open in the Choose-the-action window, read from the live master lists each time:
/// every level state (a slider), every master action and every on/off state (a tile). A state sits in its page's section, beside that
/// page's actions. The answer becomes a <see cref="TrayElement"/> by id.
internal static class TrayPicks
{
    private const string StatePrefix = "state:";
    private const string StatesHeading = "States";
    private const string ReadOnlyWords = "It can only be read, not changed from here.";
    private const string SliderNoun = "level", TileNoun = "tile";

    public static (string Title, PickerModel Model) ForSlider(IReadOnlyList<StateEntry> states, Func<string, ActionSection?> sectionOf, IPanelMembership panel) =>
        ("Choose a level for a slider", Model(states.Where(s => s.Kind == StateKind.Level).Select(s => StateChoice(s, sectionOf, needsSet: true)), SliderNoun, panel));

    public static (string Title, PickerModel Model) ForTile(IReadOnlyList<StateEntry> states, IEnumerable<HotkeyAction> actions, Func<string, ActionSection?> sectionOf, IPanelMembership panel) =>
        ("Choose an action or an on/off for a tile", Model(
            actions.Select(a => MouseActionCatalog.OmniEntry(a, unavailable: a.UnavailableReason))
                .Concat(states.Where(s => s.Kind == StateKind.OnOff && !s.OneShot).Select(s => StateChoice(s, sectionOf, needsSet: false))), TileNoun, panel));

    /// The element a pick stands for; null for an entry that is neither a master action nor a state.
    public static TrayElement? ElementOf(PickResult pick) => ElementOf(pick.Choice.Spec);

    /// The element an entry's <see cref="PickChoice.Spec"/> stands for.
    public static TrayElement? ElementOf(string spec) =>
        spec.StartsWith(StatePrefix, StringComparison.Ordinal) ? TrayElement.OfState(spec[StatePrefix.Length..])
        : MouseActionCatalog.OmniIdOf(spec) is { } id ? TrayElement.OfAction(id)
        : null;

    /// The picker entry a shown slider or tile stands for (where "Change action…" opens the list).
    public static string SpecOf(TrayTarget target) =>
        target.Action is { } action ? $"{MouseActionCatalog.Omni}:{action.Id}" : $"{StatePrefix}{target.State?.Id}";

    /// True when a stored element is the one an entry stands for (the choice and tick state of a stored one do not matter).
    public static bool Same(TrayElement stored, TrayElement picked) =>
        picked.Action is { } action ? stored.Action == action : stored.Action is null && stored.State == picked.State;

    private static PickerModel Model(IEnumerable<PickChoice> choices, string noun, IPanelMembership panel) =>
        new(PickChoice.Group(choices), null, new Dictionary<string, string>()) { Noun = noun, Panel = panel };

    /// <param name="needsSet">A slider must be able to change its level; a tile may only show a state.</param>
    private static PickChoice StateChoice(StateEntry state, Func<string, ActionSection?> sectionOf, bool needsSet) =>
        new($"{StatePrefix}{state.Id}", state.Group, state.Name, Keywords: $"state {state.Keywords}", Heading: state.Heading ?? StatesHeading,
            Place: sectionOf(state.Group), Unavailable: needsSet && state.Set is null ? ReadOnlyWords : null, Icon: state.ShownIcon, Remote: state.RemotePc);
}
