using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Devices;

/// The emulator's "until idle" as an automation rule ("Close the emulator": when No device action for 30 min, do Close the emulator): the state
/// the keeper's idle timer turns on, the rule's own action, the rule made once from the Keep running choice, and that choice read from the rule
/// (Until idle = the rule is on). UI thread.
internal sealed class EmulatorIdleRule : IDisposable
{
    public const string StateId = "devices.emulator.idle", CloseId = "devices.emulator.close", RuleName = "Close the emulator";

    private readonly EmulatorKeeper _keeper;
    private readonly IStateRegistry _states;
    private readonly FeatureRulesSlot _rules;
    private readonly Func<bool> _hasSimulation;
    private readonly string _category;

    /// <param name="hasSimulation">True while a device has a simulation method (the rule is made only then).</param>
    /// <param name="close">Closes the emulator (the rule's action).</param>
    public EmulatorIdleRule(EmulatorKeeper keeper, IActionRegistry hotkeys, IStateRegistry states, FeatureRulesSlot rules, Func<bool> hasSimulation, Func<Task> close, string category)
    {
        (_keeper, _states, _rules, _hasSimulation, _category) = (keeper, states, rules, hasSimulation, category);
        hotkeys.Register(new HotkeyAction(CloseId, RuleName, category, "smart devices simulation emulator close shut down idle memory",
            ids => Dispatcher.UIThread.Post(() => _ = Guard.RunAsync("Smart devices: close the emulator", close)), group: "Emulator") { FeatureRule = true, Icon = "device-mobile" });
        RegisterState();
        keeper.IdleChanged += OnIdleChanged;
        rules.Changed += MakeRule;
        MakeRule();
    }

    /// "No device action for 30 min": on from the keeper's idle timer until the emulator is used, started or closed again.
    private void RegisterState() => _states.Register(new StateEntry(StateId, $"No device action for {_keeper.Settings.IdleMinutes} min", _category, StateKind.OnOff,
        () => StateValue.Of(_keeper.IsIdle), "The emulator's idle timer, started after each action.", Keywords: "smart devices simulation emulator idle no action",
        Icon: "device-mobile"));

    private void OnIdleChanged() => Dispatcher.UIThread.Post(() => _states.Notify(StateId));

    /// The rule as it is now, or null (deleted).
    public FeatureRuleView? Rule => _rules.Current?.Find(CloseId);

    /// The Keep running choice: Close after each action is the keeper's own; otherwise Until idle while the rule is on, else Always.
    public EmulatorKeep Keep => _keeper.Settings.Keep == EmulatorKeep.AfterEachAction ? EmulatorKeep.AfterEachAction
        : Rule?.On == true ? EmulatorKeep.UntilIdle : EmulatorKeep.Always;

    public void SetKeep(EmulatorKeep keep)
    {
        _keeper.Settings.Keep = keep;
        _keeper.Edited();
        if (_rules.Current is not { } rules) return;
        if (Rule is null) rules.Ensure(CloseId, RuleName, keep == EmulatorKeep.UntilIdle, new FeatureTrigger.While(StateId));
        else rules.SetOn(CloseId, keep == EmulatorKeep.UntilIdle);
    }

    /// The idle minutes (the state's name says them).
    public void SetMinutes(int minutes)
    {
        _keeper.SetIdleMinutes(minutes);
        RegisterState();
    }

    public void EditRule() => _rules.Current?.Edit(CloseId);

    /// The Keep running choice becomes the rule the first time a device has a simulation method (once: a rule deleted in Automation stays deleted).
    public void MakeRule()
    {
        if (_keeper.Settings.IdleInRule || !_hasSimulation() || _rules.Current is not { } rules) return;
        _keeper.Settings.IdleInRule = true; // first: making the rule tells this again
        rules.Ensure(CloseId, RuleName, _keeper.Settings.Keep == EmulatorKeep.UntilIdle, new FeatureTrigger.While(StateId));
        _keeper.Edited();
    }

    public void Dispose()
    {
        _keeper.IdleChanged -= OnIdleChanged;
        _rules.Changed -= MakeRule;
        _states.Unregister(StateId);
    }
}
