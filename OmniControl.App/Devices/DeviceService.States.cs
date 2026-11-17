using OmniControl.App.Services;
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
            Recheck: new StateRecheck(() => CanCheckByScript(device) && AutoChecks.ReadsWhenTrayOpens(Settings, device), () => CheckStateAsync(device, scriptsOnly: true)),
            Liveness: () => LivenessOf(device, runtime)));
    }

    /// <summary>What the tray tile needs besides the value: a command or read running, the last one failed, or the value only the last one known (docs/mockups/tray-tile-states.html).</summary>
    private StateLiveness LivenessOf(DeviceRecord device, DeviceRuntime runtime)
    {
        string? notSure = null;
        if (!CanCheckByScript(device)) notSure = "Simulation only: press Check now";
        else if (runtime.KnownAt is not { } at) notSure = "Not read yet";
        else if (DateTime.Now - at >= Timing.DeviceStateStale) notSure = $"Last read {at:t}";
        return new StateLiveness(runtime.Busy && !runtime.Checking, runtime.Checking, runtime.LastFailed && !runtime.Busy ? runtime.LastLine : null, notSure);
    }

    /// <summary>The device's state as the master list of states holds it (the same one the tray tile and the list switch show); null when not known.</summary>
    public bool? PowerOf(DeviceRecord device) => _states.Find(StateId(device))?.Read() is { Number: { } on } ? on != 0 : null;

    private void ForgetState(DeviceRecord device) => _states.Unregister(StateId(device));

    /// <summary>What is known about a device changed (any thread): its state is read again.</summary>
    private void Watch(DeviceRecord device, DeviceRuntime runtime) => runtime.Changed += () =>
    {
        _states.Notify(StateId(device));
        _states.NotifyLiveness(StateId(device));
    };
}
