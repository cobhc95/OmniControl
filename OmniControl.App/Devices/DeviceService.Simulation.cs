using System.Diagnostics;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

// The simulation method: the device's own Android app is driven in an emulator over adb with the steps the user recorded, and its state is
// read with what the user taught (StateModel). All adb work is Core's (AdbClient, SimRunner); this part decides what to run, keeps the
// one-command-at-a-time rule and says how it went. The emulator shows its window only while the device's set-up is open; actions from the
// page, the list, shortcuts and rules run it hidden.
public sealed partial class DeviceService
{
    private static readonly SimTiming Pace = new(Timing.DeviceSimElementWait, Timing.DeviceSimPoll, Timing.DeviceSimSettle, Timing.DeviceSimScrollTries);

    /// <summary>The one plain sentence when a simulated device's state could not be read.</summary>
    public const string ReTeachSentence = "OmniControl couldn't tell whether it is on or off, so nothing was tapped. Teach on and off again in Edit setup.";

    /// <summary>The first thing missing before a simulation method can be used, as a short phrase; null when it can.</summary>
    private string? SimulationMissing(MethodRecord method)
    {
        if (Simulation.AdbPath is null) return "adb (the Android platform-tools) is not installed";
        if (Simulation.AddressOf(method).Length == 0) return "no emulator or adb address is set";
        if (method.Steps.Count == 0) return "no steps are recorded yet";
        if (method.Steps.Select((step, i) => SimSteps.Fix(step, i + 1)).FirstOrDefault(fix => fix is not null) is { } fix) return fix.TrimEnd('.');
        if (SimRunner.SwitchIndex(method.Steps) < 0) return "no step taps the device's switch";
        return method.Taught.IsTaught ? null : "its on and off are not taught yet";
    }

    private async Task<Attempt> RunSimulationAsync(DeviceRecord device, MethodRecord method, DevicePower power, string name)
    {
        var (client, problem) = await ReachAsync(device, method, wake: true);
        try
        {
            return client is null ? Attempt.Failed(problem!) : await RunStepsAsync(method, power, name, client);
        }
        finally { EmulatorActionDone(method); }
    }

    private async Task<Attempt> RunStepsAsync(MethodRecord method, DevicePower power, string name, AdbClient client)
    {
        var goal = power switch { DevicePower.On => SimGoal.On, DevicePower.Off => SimGoal.Off, _ => SimGoal.Toggle };
        Log($"\"{name}\" {goal} by simulation ({method.Steps.Count} steps, {Simulation.AddressOf(method)})");
        var clock = Stopwatch.StartNew();
        using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
        SimResult result;
        try
        {
            result = await SimRunner.RunAsync(client, method.Steps.ToList(), method.Taught, goal, Pace, limit.Token);
        }
        catch (OperationCanceledException)
        {
            return Attempt.Failed($"the steps took longer than {Timing.DeviceSimRunTimeout.TotalMinutes:0} minutes, so they were stopped.");
        }
        clock.Stop();
        method.Taught.Note(result.Confidence);
        Edited();
        if (result.Unsure)
        {
            var tapped = power == DevicePower.Toggle;
            var sentence = tapped ? "OmniControl tapped it but couldn't tell whether it is on or off. Teach on and off again in Edit setup." : ReTeachSentence;
            Log($"\"{name}\": {result.Problem} (confidence {result.Confidence:0.00})");
            return new Attempt(sentence, Words: tapped ? "tapped, state unknown" : "state unknown, nothing tapped", FailedStep: result.FailedIndex,
                FailedReason: result.Problem ?? string.Empty, Unsure: true);
        }
        if (!result.Ok) return new Attempt($"step {result.FailedIndex + 1}: {result.Problem}.", FailedStep: result.FailedIndex, FailedReason: result.Problem!);

        var isOn = result.IsOn == true;
        var words = result.Skipped ? $"already {(isOn ? "on" : "off")}, nothing tapped" : $"turned {(isOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s";
        Log($"\"{name}\" {words} (confidence {result.Confidence:0.00})");
        return new Attempt(null, isOn, words, Confirmed: true);
    }

    /// <summary>Runs one step on its own (the editor's Test step); null when it worked, otherwise one plain sentence.</summary>
    public async Task<string?> TestStepAsync(DeviceRecord device, MethodRecord method, SimStep step, int index)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return "It is still busy with the last command.";
        runtime.Busy = true;
        try
        {
            var (client, problem) = await ReachAsync(device, method, wake: true);
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
            EmulatorActionDone(method);
        }
    }

    /// <summary>
    /// Teaching: takes <see cref="Timing.DeviceSimTeachLooks"/> looks at the device's card while the user holds it <paramref name="on"/> or off in
    /// the emulator, keeps them and, once both states are taught, learns the signals. Null when it worked, otherwise one plain sentence.
    /// </summary>
    public async Task<string?> TeachAsync(DeviceRecord device, MethodRecord method, bool on)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return "It is still busy with the last command.";
        var at = SimRunner.SwitchIndex(method.Steps);
        if (at < 0) return "Record the steps first: the last tap is the device's switch.";
        runtime.Busy = true;
        try
        {
            var (client, problem) = await ReachAsync(device, method);
            if (client is null) return problem;
            var (samples, failed) = await SimRunner.CaptureAsync(client, method.Steps[at], method.Taught, Timing.DeviceSimTeachLooks, Timing.DeviceSimTeachGap);
            if (failed is not null)
            {
                Log($"\"{DisplayName(device)}\" teaching {(on ? "on" : "off")}: {failed}");
                return $"Couldn't look at it: {failed}.";
            }
            var list = on ? method.Taught.OnSamples : method.Taught.OffSamples;
            list.Clear();
            list.AddRange(samples);
            var learned = method.Taught.Derive();
            Edited();
            Log($"\"{DisplayName(device)}\" taught {(on ? "on" : "off")}: {(method.Taught.IsTaught ? $"tells them apart by {method.Taught.Summary}" : learned ?? "waiting for the other state")}");
            return learned is null ? null : $"{char.ToUpperInvariant(learned[0])}{learned[1..]}. Make sure the device really switched, then teach both again.";
        }
        finally { runtime.Busy = false; }
    }

    private void EmulatorActionDone(MethodRecord method)
    {
        if (Simulation.EmulatorOf(method) is { } emulator) Simulation.Keeper.ActionDone(emulator, Simulation.AddressOf(method));
    }

    /// <summary>At start-up: wakes the emulator of the first simulation method when the user asked for that, hidden.</summary>
    public void WakeEmulatorAtStart()
    {
        if (StartupMode.Inert || !Settings.Emulator.StartWithWindows || Settings.Devices.SelectMany(d => d.Methods).FirstOrDefault(m => m.IsSimulation) is not { } method) return;
        _ = Task.Run(async () =>
        {
            await Simulation.RefreshAsync();
            if (Simulation.EmulatorOf(method) is { } emulator) await Simulation.Keeper.WakeAtStartAsync(emulator, Simulation.AddressOf(method));
        });
    }

    /// <summary>
    /// Joins adb to the method's emulator and gives its client; with <paramref name="wake"/> a stopped emulator is started first, with its window
    /// only while the device's set-up is open. The problem as one plain sentence when it cannot.
    /// </summary>
    private async Task<(AdbClient? Client, string? Problem)> ReachAsync(DeviceRecord device, MethodRecord method, bool wake = false)
    {
        if (Simulation.AdbPath is not { } adb) return (null, "adb isn't installed yet.");
        var address = Simulation.AddressOf(method);
        if (address.Length == 0) return (null, "No emulator or adb address is set.");
        if (wake && Simulation.EmulatorOf(method) is { } emulator
            && await Simulation.Keeper.EnsureRunningAsync(emulator, address, show: RuntimeOf(device).InSetup) is { } wakeProblem) return (null, wakeProblem);
        if (await Simulation.ConnectAsync(address, null, _ => { }) is { } problem) return (null, problem);
        return (new AdbClient(adb, address), null);
    }

    /// <summary>Starts watching the method's emulator for a recording or a Re-pick; the watcher must be disposed. The problem is one plain sentence.</summary>
    public async Task<(SimRecorder? Recorder, string? Problem)> StartRecorderAsync(DeviceRecord device, MethodRecord method)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return (null, "It is still busy with the last command.");
        var (client, problem) = await ReachAsync(device, method);
        if (client is null) return (null, problem);
        var recorder = new SimRecorder(client, Simulation.AdbPath!, Simulation.AddressOf(method));
        if (await recorder.StartAsync() is { } failed)
        {
            recorder.Dispose();
            Log($"\"{DisplayName(device)}\" recording: {failed}");
            return (null, failed);
        }
        return (recorder, null);
    }
}
