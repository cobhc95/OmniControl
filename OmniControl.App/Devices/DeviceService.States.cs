using OmniControl.App.States;

namespace OmniControl.App.Devices;

// Each device's power in the master list of states ("L530 bulb · on/off"): made with its actions (again on a rename), removed with them,
// and notified whenever its last known state changes (a command, Check now, an auto-check). Setting it sends On or Off as the device's own
// action does (the tray flyout's tiles).
public sealed partial class DeviceService
{
    private const string HowKnown = "Its last known state: what the last command or check read (a switch by hand is seen only by Check now or Auto-check).";

    public static string StateId(DeviceRecord device) => $"devices.{device.Id}.power";

    private void RegisterState(DeviceRecord device)
    {
        var runtime = RuntimeOf(device);
        _states.Register(new StateEntry(StateId(device), DisplayName(device), Category, StateKind.OnOff,
            () => runtime.IsOn is { } on ? StateValue.Of(on, runtime.KnownAt) : null, HowKnown,
            Keywords: $"smart device plug bulb power {device.Name}", Icon: IconOf(device),
            Set: value => _ = RunAsync(device, value.Number is { } on && on != 0 ? DevicePower.On : DevicePower.Off),
            Recheck: new StateRecheck(() => CanCheckByScript(device), () => CheckStateAsync(device, scriptsOnly: true))));
    }

    private void ForgetState(DeviceRecord device) => _states.Unregister(StateId(device));

    /// <summary>What is known about a device changed (any thread): its state is read again.</summary>
    private void Watch(DeviceRecord device, DeviceRuntime runtime) => runtime.Changed += () => _states.Notify(StateId(device));
}
