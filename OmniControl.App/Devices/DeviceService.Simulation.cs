using System.Diagnostics;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

// The simulation method: the device's own Android app is driven in an emulator over adb with the steps the user recorded. All adb work is
// Core's (AdbClient, SimRunner); this part decides what to run, keeps the one-command-at-a-time rule and says how it went.
public sealed partial class DeviceService
{
    private static readonly SimTiming Pace = new(Timing.DeviceSimElementWait, Timing.DeviceSimPoll, Timing.DeviceSimSettle);

    /// <summary>The first thing missing before a simulated device can be switched, as a short phrase; null when it is ready.</summary>
    private string? SimulationMissing(DeviceRecord device)
    {
        if (Simulation.AdbPath is null) return "adb (the Android platform-tools) is not installed";
        if (Simulation.AddressOf(device).Length == 0) return "no emulator or adb address is set";
        if (device.Steps.Count == 0) return "no steps are recorded yet";
        return device.Steps.Select(SimSteps.Missing).FirstOrDefault(m => m is not null) is { } step ? $"a step is incomplete ({step})" : null;
    }

    private async Task<string?> RunSimulationAsync(DeviceRecord device, DevicePower power, DeviceRuntime runtime, string name)
    {
        var (client, problem) = await ReachAsync(device);
        if (client is null) return Fail(runtime, name, problem!);
        var goal = power switch { DevicePower.On => SimGoal.On, DevicePower.Off => SimGoal.Off, _ => SimGoal.Toggle };
        Log($"\"{name}\" {goal} by simulation ({device.Steps.Count} steps, {Simulation.AddressOf(device)})");
        var clock = Stopwatch.StartNew();
        using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
        SimResult result;
        try
        {
            result = await SimRunner.RunAsync(client, device.Steps.ToList(), goal, Pace, limit.Token);
        }
        catch (OperationCanceledException)
        {
            return Fail(runtime, name, $"the steps took longer than {Timing.DeviceSimRunTimeout.TotalMinutes:0} minutes, so they were stopped.");
        }
        clock.Stop();
        if (!result.Ok) return Fail(runtime, name, $"step {result.FailedIndex + 1}: {result.Problem}.", result.FailedIndex, result.Problem!);

        var isOn = result.IsOn ?? power switch { DevicePower.On => true, DevicePower.Off => false, _ => runtime.IsOn != true };
        var words = result.Skipped ? $"already {(isOn ? "on" : "off")}, nothing tapped" : $"turned {(isOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s";
        runtime.Finished(isOn, $"Last run {DateTime.Now:t} · {words}", failed: false);
        Log($"\"{name}\" {words}");
        return null;
    }

    /// <summary>Runs one step on its own (the editor's Test step); null when it worked, otherwise one plain sentence.</summary>
    public async Task<string?> TestStepAsync(DeviceRecord device, SimStep step, int index)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return "It is still busy with the last command.";
        runtime.Busy = true;
        try
        {
            var (client, problem) = await ReachAsync(device);
            if (client is null) return problem;
            using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
            var result = await SimRunner.TestStepAsync(client, step, index, Pace, limit.Token);
            Log($"\"{DisplayName(device)}\" step {index + 1} test: {result.Problem ?? "worked"}");
            return result.Problem is null ? null : $"Step {index + 1}: {result.Problem}.";
        }
        catch (OperationCanceledException)
        {
            return "The step took too long and was stopped.";
        }
        finally
        {
            runtime.Busy = false;
        }
    }

    /// <summary>Joins adb to the device's emulator and gives its client; the problem as one plain sentence when it cannot.</summary>
    private async Task<(AdbClient? Client, string? Problem)> ReachAsync(DeviceRecord device)
    {
        if (Simulation.AdbPath is not { } adb) return (null, "adb isn't installed yet.");
        var address = Simulation.AddressOf(device);
        if (address.Length == 0) return (null, "No emulator or adb address is set.");
        if (await Simulation.ConnectAsync(address) is { } problem) return (null, problem);
        return (new AdbClient(adb, address), null);
    }

    /// <summary>Starts watching the device's emulator for a recording or a Re-pick; the watcher must be disposed. The problem is one plain sentence.</summary>
    public async Task<(SimRecorder? Recorder, string? Problem)> StartRecorderAsync(DeviceRecord device)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return (null, "It is still busy with the last command.");
        var (client, problem) = await ReachAsync(device);
        if (client is null) return (null, problem);
        var recorder = new SimRecorder(client, Simulation.AdbPath!, Simulation.AddressOf(device));
        if (await recorder.StartAsync() is { } failed)
        {
            recorder.Dispose();
            Log($"\"{DisplayName(device)}\" recording: {failed}");
            return (null, failed);
        }
        return (recorder, null);
    }
}
