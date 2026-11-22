using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Ui.Controls;

namespace OmniControl.App.Features.Automation;

// States with a side list (StateEntry.Menu, the network states) are one picker row "<menu> ▸" that opens it beside the row (the shared
// menu rows, as the traffic page's network list): its heading and first part, a thin line, its middle part, then a find box over the muted rest.
public static partial class AutomationTriggers
{
    private const string MenuSpec = StateChanges + ".menu:";

    /// The states as a picker lists them: each one, except a side list's, which is one row where its first state would be.
    private static IEnumerable<PickChoice> Listed(ITriggerSources sources, Func<StateEntry, PickChoice> choiceOf, Func<StateMenu, PickChoice, PickChoice> place)
    {
        var menus = new HashSet<StateMenu>();
        foreach (var state in sources.States)
            if (state.Menu is not { } menu) yield return choiceOf(state);
            else if (menus.Add(menu)) yield return place(menu, MenuChoice(menu, sources, choiceOf));
    }

    private static PickChoice MenuChoice(StateMenu menu, ITriggerSources sources, Func<StateEntry, PickChoice> choiceOf)
    {
        IReadOnlyList<StateEntry> States() => sources.States.Where(s => s.Menu == menu).ToList();
        var words = string.Join(' ', States().Select(s => $"{s.Name} {s.ShortName} {s.Note} {s.Keywords}"));
        return new PickChoice(MenuSpec + menu.Name, StatesSection, $"{menu.Name} ▸", Keywords: $"state {words}", Command: async from =>
        {
            if (from is not Control at) return Array.Empty<PickChoice>();
            StateEntry? picked = null;
            var find = string.Empty;
            await MenuRowList.ShowBesideAsync(at, () => MenuRows(menu, States(), find, text => find = text, s => picked = s));
            return picked is null ? Array.Empty<PickChoice>() : new[] { choiceOf(picked) };
        });
    }

    private static IReadOnlyList<MenuRowItem> MenuRows(StateMenu menu, IReadOnlyList<StateEntry> states, string find, Action<string> setFind, Action<StateEntry> pick)
    {
        MenuRowItem Row(StateEntry s, bool ruleAbove = false, bool dim = false) =>
            new(s.ShortName, string.Empty, false, string.Empty, new RelayCommand(() => pick(s)), RuleAbove: ruleAbove, Dim: dim, Trailing: s.Note ?? string.Empty);
        var rows = new List<MenuRowItem>();
        var top = states.Where(s => s.MenuPart == StateMenuPart.Top).ToList();
        if (top.Count > 0) rows.Add(MenuRowItem.Header(menu.Heading));
        rows.AddRange(top.Select(s => Row(s)));
        var line = rows.Count > 0;
        rows.AddRange(states.Where(s => s.MenuPart == StateMenuPart.Middle).Select((s, i) => Row(s, ruleAbove: i == 0 && line)));
        var rest = states.Where(s => s.MenuPart == StateMenuPart.Rest).ToList();
        if (rest.Count == 0) return rows;
        rows.Add(new MenuRowItem(menu.Find, string.Empty, false, string.Empty, new RelayCommand(() => { }), RuleAbove: true, Trailing: $"{rest.Count} more", Find: setFind));
        rows.AddRange(rest.Where(s => s.ShortName.Contains(find, StringComparison.CurrentCultureIgnoreCase)).Select(s => Row(s, dim: true)));
        return rows;
    }
}
