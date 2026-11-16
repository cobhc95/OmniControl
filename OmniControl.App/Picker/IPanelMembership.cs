namespace OmniControl.App.Picker;

/// <summary>
/// What a picker opened from a panel (the tray flyout) knows about it: which entries are in the panel already, and a way to take one out.
/// The picker marks those entries and offers "Remove from panel" / "Add again"; it never learns what the panel is. Other pickers have none.
/// </summary>
public interface IPanelMembership
{
    /// <summary>True when the entry with this <see cref="PickChoice.Spec"/> is in the panel now.</summary>
    bool Contains(string spec);

    /// <summary>Takes the entry out of the panel at once (the panel saves it); <see cref="Changed"/> follows.</summary>
    void Remove(string spec);

    /// <summary>The panel's content changed.</summary>
    event Action? Changed;
}
