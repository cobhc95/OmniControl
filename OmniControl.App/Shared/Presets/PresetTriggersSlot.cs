using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Shared.Presets;

/// One trigger chip of a preset row: its words and the join before it ("or" / "and"; empty for the first).
public sealed record PresetTrigger(string Text, string Join);

/// The triggers that switch to a preset (a state, an action), kept as automation rules by the feature that owns rules. Every preset is
/// named by the id of its apply action (<see cref="PresetShortcuts.ActionId"/>).
public interface IPresetTriggers
{
    /// A rule, a state or an action changed: the chips and the pickers read again.
    event Action? Changed;

    /// The apply actions of the presets that have triggers (each must stay in the master list while it has some).
    IEnumerable<string> Presets { get; }

    IReadOnlyList<PresetTrigger> Of(string preset);

    /// The master list of states (live), sectioned by the page each belongs to.
    IReadOnlyList<PickChoice> StateChoices();

    /// The master list of actions (live): the preset switches when the picked one runs.
    IReadOnlyList<PickChoice> ActionChoices();

    /// Adds a picked trigger (as a picker stores it); <paramref name="title"/> names the rule.
    void Add(string preset, string title, string stored);

    void Remove(string preset, int index);

    /// Flips the join before trigger <paramref name="index"/> between "or" and "and".
    void FlipJoin(string preset, int index);
}

/// What the rules ask of the presets: the preset in use in the same group (put back, through its apply action, when a state ends), whether a
/// preset still exists, and which presets need their apply action kept in the master list.
public interface IPresetSwitch
{
    /// A group's presets changed (its id): one may have been deleted or brought back by an Undo.
    event Action<string>? Changed;

    string? InUse(string preset);

    /// False when its group is registered and has no such preset (deleted); null when its group is not registered now.
    bool? Exists(string preset);

    void Keep(IEnumerable<string> presets);
}

/// Where the rules plug in their <see cref="IPresetTriggers"/> (made by the shell, as <c>ActionGateSlot</c> is): the preset manager reads it,
/// empty while automation is switched off.
public sealed class PresetTriggersSlot(IPresetSwitch presets)
{
    public IPresetSwitch Presets => presets;

    public IPresetTriggers? Current { get; private set; }

    /// Triggers came, went or changed.
    public event Action? Changed;

    public void Attach(IPresetTriggers triggers)
    {
        Current = triggers;
        triggers.Changed += OnChanged;
        OnChanged();
    }

    public void Detach(IPresetTriggers triggers)
    {
        if (!ReferenceEquals(Current, triggers)) return;
        triggers.Changed -= OnChanged;
        Current = null;
        OnChanged();
    }

    private void OnChanged()
    {
        presets.Keep(Current?.Presets ?? Array.Empty<string>());
        Changed?.Invoke();
    }
}
