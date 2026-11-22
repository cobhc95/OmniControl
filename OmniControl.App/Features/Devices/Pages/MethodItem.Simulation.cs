using OmniControl.App.Features.Devices;

namespace OmniControl.App.Features.Devices.Pages;

// The simulation method's set-up stages: 1 Emulator and connection, 2 App ready,
// 3 Steps, 4 Teach on and off, 5 Test.
public sealed partial class MethodItem
{
    public const int SimulationTeachStage = 4;

    private IReadOnlyList<SetupStageItem> MakeSimulationStages(Action<SetupStageItem> open)
    {
        var setup = _service.Simulation;
        return new[]
        {
            new SetupStageItem(1, DeviceStages.Emulator,
                () => setup.EmulatorOf(Record) is { } e && setup.IsInstalled(e) && setup.AdbPath is not null && setup.IsConnected(setup.AddressOf(Record)), ConnectionSummary, open),
            new SetupStageItem(2, DeviceStages.AppReady, () => Record.SimAppReady, () => Record.SimAppReady ? AppName() : "not marked yet", open),
            new SetupStageItem(3, DeviceStages.Steps, () => Record.Steps.Count > 0 && Record.Steps.All(s => SimSteps.Missing(s) is null) && SimRunner.SwitchIndex(Record.Steps) >= 0, StepsSummary, open),
            new SetupStageItem(SimulationTeachStage, DeviceStages.Teach, () => Record.Taught.IsTaught,
                () => Record.Taught.IsTaught ? $"told apart by {Record.Taught.Summary}" : "not taught yet", open),
            new SetupStageItem(5, DeviceStages.Test, () => OnPassed && OffPassed && TogglePassed,
                () => OnPassed && OffPassed && TogglePassed ? "On, Off and Toggle each worked" : "On, Off, Toggle each checked against the taught state", open),
        };
    }

    private string ConnectionSummary()
    {
        var setup = _service.Simulation;
        if (setup.AdbPath is null) return "adb not found";
        if (setup.EmulatorOf(Record) is not { } emulator || !setup.IsInstalled(emulator)) return "no emulator found";
        return $"{emulator.Name} · {(setup.IsConnected(setup.AddressOf(Record)) ? "connected" : "not connected")}";
    }

    /// The app the steps open (its name, else its package).
    private string AppName() =>
        Record.Steps.FirstOrDefault(s => s.Kind == SimStepKind.OpenApp) is { } open ? (open.Label.Length > 0 ? open.Label : open.Package) : "done";

    /// The steps as one short line: "Open Tapo · Tap Tapo L530's power button".
    private string StepsSummary()
    {
        if (Record.Steps.Count == 0) return "no steps yet";
        if (Record.Steps.Select((step, i) => SimSteps.Fix(step, i + 1)).FirstOrDefault(fix => fix is not null) is { } fix) return fix;
        return string.Join(" · ", Record.Steps.Select(step =>
        {
            var words = SimSteps.Words(step);
            var target = words.Target.Trim('"');
            return step.Kind switch
            {
                SimStepKind.OpenApp => $"Open {target}",
                SimStepKind.Tap => $"Tap {target}",
                _ => $"{words.Lead} {target}",
            };
        }));
    }

    public bool NeedsAdb => _service.Simulation.AdbPath is null;

    [RelayCommand]
    private void MarkAppReady()
    {
        Record.SimAppReady = true;
        _service.Edited();
        AdvanceSetup();
    }

    [RelayCommand]
    private void GetAdb()
    {
        if (_service.Simulation.OpenAdbPage(_report) is { } problem) _report(problem);
    }
}
