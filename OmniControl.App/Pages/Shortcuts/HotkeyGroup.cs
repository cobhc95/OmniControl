using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>The hotkeys of one feature (Displays, OSD...), shown together in a collapsible panel.</summary>
public sealed class HotkeyGroup : CardGroup
{
    public IReadOnlyList<HotkeyAction> Actions { get; }

    public HotkeyGroup(string name, IReadOnlyList<HotkeyAction> actions)
        : base("Shortcuts/" + name, name, actions.Count == 1 ? "1 shortcut" : $"{actions.Count} shortcuts") => Actions = actions;
}
