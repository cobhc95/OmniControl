using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>
/// The hotkeys of one feature (Displays, OSD...), shown together in a collapsible panel. Actions with a <see cref="HotkeyAction.Subgroup"/>
/// sit in a collapsed panel of their own inside it; the others stay directly in the panel.
/// </summary>
public sealed class HotkeyGroup : CardGroup
{
    public IReadOnlyList<HotkeyAction> Actions { get; }

    public IReadOnlyList<HotkeySubgroup> Subgroups { get; }

    public bool HasActions => Actions.Count > 0;

    public HotkeyGroup(string name, IReadOnlyList<HotkeyAction> actions)
        : base("Shortcuts/" + name, name, actions.Count == 1 ? "1 shortcut" : $"{actions.Count} shortcuts")
    {
        Actions = actions.Where(a => a.Subgroup is null).ToList();
        Subgroups = actions.Where(a => a.Subgroup is not null).GroupBy(a => a.Subgroup!)
            .Select(g => new HotkeySubgroup($"Shortcuts/{name}/{g.Key}", g.Key, g.ToList())).ToList();
    }
}

/// <summary>One subgroup of a <see cref="HotkeyGroup"/>: its own header and cards, collapsed until the user opens it.</summary>
public sealed class HotkeySubgroup : CardGroup
{
    public IReadOnlyList<HotkeyAction> Actions { get; }

    public HotkeySubgroup(string key, string name, IReadOnlyList<HotkeyAction> actions)
        : base(key, name, actions.Count == 1 ? "1 shortcut" : $"{actions.Count} shortcuts") => Actions = actions;
}
