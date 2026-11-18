namespace OmniControl.App.Hotkeys;

/// <summary>
/// The On / Off / Toggle family of something with an on/off state: the owner keeps its existing "Toggle ..." action (same id, so keys and rules
/// keep working), marks it <see cref="Mark"/>ed, and registers the On and Off actions from <see cref="Siblings"/>. All three are one row in the
/// pickers (Direction setting), and On and Off take the set-to-state check of <c>HotkeyRegistry.RunNow</c> through <see cref="HotkeyAction.StateId"/>.
/// </summary>
public sealed record OnOffFamily(string Id, string Name, string StateId)
{
    public const string ToggleRole = "Toggle";

    public ActionFamily Family => new(Id, Name);

    /// <summary>The id of the On or Off action next to a toggle action.</summary>
    public static string SiblingId(string toggleId, bool on) => $"{toggleId}.{(on ? "on" : "off")}";

    /// <summary>Both sibling ids (for the owner's unregister).</summary>
    public static IEnumerable<string> SiblingIds(string toggleId) => new[] { SiblingId(toggleId, true), SiblingId(toggleId, false) };

    /// <summary>The family and role a toggle action carries; put both in its initialiser.</summary>
    public (ActionFamily Family, string Role) Mark => (Family, ToggleRole);

    /// <summary>The "<c>Name</c>: On" and "<c>Name</c>: Off" actions, placed and drawn like <paramref name="toggle"/>; <paramref name="set"/> puts the thing in that state.</summary>
    public IEnumerable<HotkeyAction> Siblings(HotkeyAction toggle, Action<bool> set) =>
        new[] { true, false }.Select(on => new HotkeyAction(SiblingId(toggle.Id, on), $"{Name}: {(on ? "On" : "Off")}", toggle.Category, toggle.Keywords, _ => set(on), group: toggle.Group, subgroup: toggle.Subgroup)
        {
            Icon = toggle.Icon,
            SuggestedIcons = toggle.SuggestedIcons,
            PickerSection = toggle.PickerSection,
            NeverBlock = toggle.NeverBlock,
            Family = Family,
            FamilyRole = on ? StateTarget.OnRole : StateTarget.OffRole,
            StateId = StateId,
        });
}
