namespace OmniControl.App.Shared.Presets;

/// The automation rule that switches to a preset, as its row tells of it: its triggers in words ("When Steam is running or at 22:00"); null: none.
public sealed record PresetRule(string Words);

/// The automation rules that switch to a preset (a rule holding the preset's apply action, made and changed only in Automation), read from
/// the feature that owns rules. Every preset is named by the id of its apply action (<see cref="PresetShortcuts.ActionId"/>).
public interface IPresetTriggers
{
    /// A rule, a state or an action changed: the rows read again.
    event Action? Changed;

    /// The preset's rule, or null when none holds its apply action.
    PresetRule? Of(string preset);

    /// Opens Automation on the preset's rule, or where a rule for it is made when it has none.
    void Edit(string preset);
}

/// What the rules ask of the presets: the preset in use in the same group (put back, through its apply action, when a state ends), whether a
/// preset still exists.
public interface IPresetSwitch
{
    /// A group's presets changed (its id): one may have been deleted or brought back by an Undo.
    event Action<string>? Changed;

    string? InUse(string preset);

    /// False when its group is registered and has no such preset (deleted); null when its group is not registered now.
    bool? Exists(string preset);

    /// The group a preset's apply action belongs to, or null when its group is not registered now.
    PresetGroup? GroupOf(string preset);
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

    private void OnChanged() => Changed?.Invoke();
}
