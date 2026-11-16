using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Devices;

// The simulation method's set-up checklist (docs/mockups/devices-simulation.html section 1): the same rows and look as the scripts method's.
public sealed partial class DeviceItem
{
    private IReadOnlyList<SetupStepItem> MakeSimulationChecklist()
    {
        var setup = _service.Simulation;
        return new[]
        {
            new SetupStepItem(() => setup.EmulatorOf(Record) is { } emulator && setup.IsInstalled(emulator),
                () => setup.EmulatorOf(Record) is { } emulator && setup.IsInstalled(emulator) ? $"Emulator found: {emulator.Name}" : "No emulator found (BlueStacks, Android Studio or LDPlayer)")
            {
                ActionCommand = new RelayCommand(GetEmulator),
                ActionTip = "Open the official download page of an emulator. Install it, then come back.",
            },
            new SetupStepItem(() => setup.AdbPath is not null, () => setup.AdbPath is not null ? "adb found" : "adb (Android SDK platform-tools) not found")
            {
                ActionCommand = new RelayCommand(GetAdb),
                ActionTip = "Open the official Android page for platform-tools. Unpack it and add its folder to the PATH, or put it in your Android SDK folder.",
            },
            new SetupStepItem(() => setup.IsConnected(setup.AddressOf(Record)), () => $"adb connected to {setup.AddressOf(Record)}")
            {
                ActionCommand = Simulation.ConnectCommand,
                ActionTip = "Join adb to the emulator (it must be open, with adb switched on).",
            },
            new SetupStepItem(() => Record.Steps.Any(s => s.Kind == SimStepKind.OpenApp), () => "The device's app is set up inside the emulator")
            {
                ActionCommand = Simulation.OpenEmulatorCommand,
                ActionTip = "Open the emulator, install the device's app (for example Tapo) from its store and sign in there. OmniControl never sees that password.",
            },
            new SetupStepItem(() => Record.Steps.Count > 0 && Record.Steps.All(s => SimSteps.Missing(s) is null), () => "Steps recorded"),
            new SetupStepItem(() => Record.SimVerified, () => "Tested") { ActionCommand = TestCommand, ActionTip = "Switches the device off, then on again." },
        };
    }

    private void GetEmulator()
    {
        var emulator = _service.Simulation.EmulatorOf(Record) ?? Emulators.All[0];
        if (_service.Simulation.OpenDownloadPage(emulator) is { } problem) _report(problem);
    }

    private void GetAdb()
    {
        if (_service.Simulation.OpenAdbPage() is { } problem) _report(problem);
    }

    private void RefreshSimulationChecklist()
    {
        var setup = _service.Simulation;
        RefreshChecklist(_simulationSteps);
        _simulationSteps[0].ActionLabel = setup.EmulatorOf(Record) is { } emulator && setup.IsInstalled(emulator) ? string.Empty : "Get it";
        _simulationSteps[1].ActionLabel = setup.AdbPath is null ? "Get adb" : string.Empty;
        _simulationSteps[2].ActionLabel = setup.IsConnected(setup.AddressOf(Record)) ? string.Empty : "Connect";
        _simulationSteps[2].ActionEnabled = Simulation.CanConnect;
        _simulationSteps[3].ActionLabel = Simulation.CanOpenEmulator ? "Open emulator" : string.Empty;
        _simulationSteps[5].ActionLabel = Record.SimVerified ? string.Empty : "Test";
        _simulationSteps[5].ActionEnabled = CanSwitch;
    }
}
