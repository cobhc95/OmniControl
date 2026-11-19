namespace OmniControl.App.Features.Hotkeys;

/// Where the one <see cref="IActionGate"/> plugs in: made by the shell and given to <see cref="HotkeyRegistry"/>, which asks it before every
/// run, and to the feature that answers (Automation), which attaches itself when made and detaches when it goes. Empty = nothing is refused.
public sealed class ActionGateSlot
{
    private volatile IActionGate? _gate;

    public void Attach(IActionGate gate) => _gate = gate;

    /// Takes <paramref name="gate"/> out, if it is the one attached.
    public void Detach(IActionGate gate)
    {
        if (ReferenceEquals(_gate, gate)) _gate = null;
    }

    /// Why the action must not run now, or null when it may (or no gate is attached).
    public string? BlockReason(HotkeyAction action) => _gate?.BlockReason(action);
}
