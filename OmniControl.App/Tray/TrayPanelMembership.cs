using OmniControl.App.Picker;

namespace OmniControl.App.Tray;

/// <summary>What the panel's list (sliders or tiles) tells the Choose-the-action window opened from the flyout: which entries are in it, and taking one out.</summary>
internal sealed class TrayPanelMembership : IPanelMembership
{
    private readonly Func<IReadOnlyList<TrayElement>> _inPanel;
    private readonly Action<TrayElement> _remove;

    /// <param name="inPanel">The stored elements now.</param>
    /// <param name="remove">Takes one stored element out and saves.</param>
    public TrayPanelMembership(Func<IReadOnlyList<TrayElement>> inPanel, Action<TrayElement> remove) => (_inPanel, _remove) = (inPanel, remove);

    public event Action? Changed;

    public bool Contains(string spec) => TrayPicks.ElementOf(spec) is { } picked && _inPanel().Any(e => TrayPicks.Same(e, picked));

    public void Remove(string spec)
    {
        if (TrayPicks.ElementOf(spec) is not { } picked) return;
        foreach (var stored in _inPanel().Where(e => TrayPicks.Same(e, picked)).ToList()) _remove(stored);
        Changed?.Invoke();
    }
}
