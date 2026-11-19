namespace OmniControl.App.Features.Hotkeys.Picker;

/// What a picker opened from a panel (the tray flyout) knows about it: which entries are in the panel already, and a way to take one out.
/// The picker marks those entries and offers "Remove from panel" / "Add again"; it never learns what the panel is. Other pickers have none.
public interface IPanelMembership
{
    /// True when the entry with this <see cref="PickChoice.Spec"/> is in the panel now.
    bool Contains(string spec);

    /// Takes the entry out of the panel at once (the panel saves it); <see cref="Changed"/> follows.
    void Remove(string spec);

    /// The panel's content changed.
    event Action? Changed;
}
