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
            Liveness: () => LivenessOf(device, runtime), Common: CommonGroups.SmartDevices));
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

    // The emulator's running state ("Emulator · on/off"): one entry for the simulation methods' emulator. Its owner is this service because it
    // knows the methods; the keeper only does the starting and closing. Setting it starts the emulator hidden or closes it.
    public const string EmulatorStateId = "devices.emulator.running";

    private void RegisterEmulatorState()
    {
        _states.Register(new StateEntry(EmulatorStateId, "Emulator", Category, StateKind.OnOff,
            () => StateValue.Of(EmulatorIsUp(), DateTime.Now), "Whether adb reaches the simulation method's emulator, looked at every few seconds.",
            Keywords: "smart devices simulation emulator android running memory", Icon: "device-mobile",
            Set: value => _ = SetEmulatorAsync(value.Number is { } on && on != 0),
            Liveness: () => new StateLiveness(Simulation.Keeper.Busy, false, null, null)));
        Simulation.Changed += () =>
        {
            _states.Notify(EmulatorStateId);
            _states.NotifyLiveness(EmulatorStateId);
        };
    }

    private IEnumerable<MethodRecord> SimulationMethods => Settings.Devices.SelectMany(d => d.Methods).Where(m => m.IsSimulation);

    private bool EmulatorIsUp() => SimulationMethods.Any(m => Simulation.IsConnected(Simulation.AddressOf(m)));

    private async Task SetEmulatorAsync(bool on)
    {
        if (SimulationMethods.FirstOrDefault() is not { } method || Simulation.EmulatorOf(method) is not { } emulator) return;
        var address = Simulation.AddressOf(method);
        var problem = on ? await Simulation.Keeper.StartAsync(emulator, address) : await Simulation.Keeper.CloseNowAsync(emulator, address);
        if (problem is not null) _report(problem);
    }
}
