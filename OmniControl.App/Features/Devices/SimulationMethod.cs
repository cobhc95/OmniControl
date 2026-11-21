using System.Diagnostics;

namespace OmniControl.App.Features.Devices;

/// The simulation method: the device's own Android app is driven in an emulator over adb with the steps the user recorded, and its state is
/// read with what the user taught (StateModel). All adb work is Core's (AdbClient, SimRunner); this class decides what to run, keeps the
/// one-command-at-a-time rule and says how it went. The emulator shows its window only while the device's set-up is open; actions from the
/// page, the list, shortcuts and rules run it hidden. The emulator and adb themselves are <see cref="SimulationSetup"/>'s.
public sealed class SimulationMethod
{
    /// The one plain sentence when a simulated device's state could not be read.
    public const string ReTeachSentence = "OmniControl couldn't tell whether it is on or off, so nothing was tapped. Teach on and off again in Edit setup.";

    private const string BusySentence = "It is still busy with the last command.";

    private readonly SimulationSetup _setup;
    private readonly Func<DeviceRecord, DeviceRuntime> _runtimeOf;
    private readonly Action _edited;

    /// <param name="runtimeOf">A device's live state (busy, in its set-up).</param>
    /// <param name="edited">Saves the device list after what was taught or how sure a reading was changed.</param>
    internal SimulationMethod(SimulationSetup setup, Func<DeviceRecord, DeviceRuntime> runtimeOf, Action edited)
    {
        _setup = setup;
        _runtimeOf = runtimeOf;
        _edited = edited;
    }

    private static SimTiming Pace => new(Timing.DeviceSimElementWait, Timing.DeviceSimPoll, Timing.DeviceSimSettle, Timing.DeviceSimScrollTries, DeviceService.Log);

    /// The first thing missing before a simulation method can be used, as a short phrase; null when it can.
    internal string? Missing(MethodRecord method)
    {
        if (_setup.AdbPath is null) return "adb (the Android platform-tools) is not installed";
        if (_setup.AddressOf(method).Length == 0) return "no emulator or adb address is set";
        if (method.Steps.Count == 0) return "no steps are recorded yet";
        if (method.Steps.Select((step, i) => SimSteps.Fix(step, i + 1)).FirstOrDefault(fix => fix is not null) is { } fix) return fix.TrimEnd('.');
        if (SimRunner.SwitchIndex(method.Steps) < 0) return "no step taps the device's switch";
        return method.Taught.IsTaught ? null : "its on and off are not taught yet";
    }

    internal async Task<Attempt> RunAsync(DeviceRecord device, MethodRecord method, DevicePower power, string name, CancellationToken stop)
    {
        var (client, problem) = await ReachAsync(device, method, wake: true);
        try
        {
            return client is null ? Attempt.Failed(problem!) : stop.IsCancellationRequested ? Attempt.Failed(DeviceService.StoppedSentence) : await RunStepsAsync(method, power, name, client, stop);
        }
        finally { ActionDone(method); }
    }

    private async Task<Attempt> RunStepsAsync(MethodRecord method, DevicePower power, string name, AdbClient client, CancellationToken stop)
    {
        var goal = power switch { DevicePower.On => SimGoal.On, DevicePower.Off => SimGoal.Off, _ => SimGoal.Toggle };
        DeviceService.Log($"\"{name}\" {goal} by simulation ({method.Steps.Count} steps, {_setup.AddressOf(method)})");
        var clock = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(Timing.DeviceSimRunTimeout);
        SimResult result;
        try
        {
            result = await SimRunner.RunAsync(client, method.Steps.ToList(), method.Taught, goal, Pace, limit.Token);
        }
        catch (OperationCanceledException)
        {
            if (stop.IsCancellationRequested) return Attempt.Failed(DeviceService.StoppedSentence);
            return Attempt.Failed($"the steps took longer than {Timing.DeviceSimRunTimeout.TotalMinutes:0} minutes, so they were stopped.");
        }
        clock.Stop();
        method.Taught.Note(result.Confidence);
        _edited();
        if (result.Unsure)
        {
            var tapped = power == DevicePower.Toggle;
            var sentence = tapped ? "OmniControl tapped it but couldn't tell whether it is on or off. Teach on and off again in Edit setup." : ReTeachSentence;
            DeviceService.Log($"\"{name}\": {result.Problem} (confidence {result.Confidence:0.00})");
            return new Attempt(sentence, Words: tapped ? "tapped, state unknown" : "state unknown, nothing tapped", FailedStep: result.FailedIndex,
                FailedReason: result.Problem ?? string.Empty, Unsure: true);
        }
        if (!result.Ok) return new Attempt($"step {result.FailedIndex + 1}: {result.Problem}.", FailedStep: result.FailedIndex, FailedReason: result.Problem!);

        var isOn = result.IsOn == true;
        var words = result.Skipped ? $"already {(isOn ? "on" : "off")}, nothing tapped" : $"turned {(isOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s";
        DeviceService.Log($"\"{name}\" {words} (confidence {result.Confidence:0.00})");
        return new Attempt(null, isOn, words, Confirmed: true);
    }

    /// Reads the taught state off the app's screen (the emulator is woken hidden); nothing is tapped.
    internal async Task<Attempt> CheckAsync(DeviceRecord device, MethodRecord method, string name)
    {
        var (client, problem) = await ReachAsync(device, method, wake: true);
        if (client is null)
        {
            ActionDone(method);
            return Attempt.Failed(problem!);
        }
        try
        {
            using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
            var result = await SimRunner.ReadStateAsync(client, method.Steps.ToList(), method.Taught, Pace, limit.Token);
            method.Taught.Note(result.Confidence);
            _edited();
            if (result.Unsure)
            {
                DeviceService.Log($"\"{name}\" check: {result.Problem} (confidence {result.Confidence:0.00})");
                return new Attempt(ReTeachSentence, FailedStep: result.FailedIndex, FailedReason: result.Problem ?? string.Empty, Unsure: true);
            }
            return result.Ok ? new Attempt(null, result.IsOn == true) : Attempt.Failed($"step {result.FailedIndex + 1}: {result.Problem}.");
        }
        catch (OperationCanceledException)
        {
            return Attempt.Failed($"the check took longer than {Timing.DeviceSimRunTimeout.TotalMinutes:0} minutes, so it was stopped.");
        }
        finally { ActionDone(method); }
    }

    /// Runs one step on its own (the editor's Test step); null when it worked, otherwise one plain sentence.
    public async Task<string?> TestStepAsync(DeviceRecord device, MethodRecord method, SimStep step, int index)
    {
        var runtime = _runtimeOf(device);
        if (runtime.Busy) return BusySentence;
        runtime.Busy = true;
        try
        {
            var (client, problem) = await ReachAsync(device, method, wake: true);
            if (client is null) return problem;
            using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
            var result = await SimRunner.TestStepAsync(client, step, index, Pace, limit.Token);
            DeviceService.Log($"\"{DeviceService.DisplayName(device)}\" step {index + 1} test: {result.Problem ?? "worked"}");
            return result.Problem is null ? null : $"Step {index + 1}: {result.Problem}.";
        }
        catch (OperationCanceledException)
        {
            return "The step took too long and was stopped.";
        }
        finally
        {
            runtime.Busy = false;
            ActionDone(method);
        }
    }

    /// Teaching: takes <see cref="Timing.DeviceSimTeachLooks"/> looks at the device's card while the user holds it <paramref name="on"/> or off in
    /// the emulator, keeps them and, once both states are taught, learns the signals. Null when it worked, otherwise one plain sentence.
    public async Task<string?> TeachAsync(DeviceRecord device, MethodRecord method, bool on)
    {
        var runtime = _runtimeOf(device);
        if (runtime.Busy) return BusySentence;
        var at = SimRunner.SwitchIndex(method.Steps);
        if (at < 0) return "Record the steps first: the last tap is the device's switch.";
        runtime.Busy = true;
        try
        {
            var (client, problem) = await ReachAsync(device, method);
            if (client is null) return problem;
            var (samples, failed) = await SimRunner.CaptureAsync(client, method.Steps[at], method.Taught, Timing.DeviceSimTeachLooks, Timing.DeviceSimTeachGap, DeviceService.Log);
            var name = DeviceService.DisplayName(device);
            if (failed is not null)
            {
                DeviceService.Log($"\"{name}\" teaching {(on ? "on" : "off")}: {failed}");
                return $"Couldn't look at it: {failed}.";
            }
            var list = on ? method.Taught.OnSamples : method.Taught.OffSamples;
            list.Clear();
            list.AddRange(samples);
            var learned = method.Taught.Derive();
            _edited();
            DeviceService.Log($"\"{name}\" taught {(on ? "on" : "off")}: {(method.Taught.IsTaught ? $"tells them apart by {method.Taught.Summary}" : learned ?? "waiting for the other state")}");
            return learned is null ? null : $"{char.ToUpperInvariant(learned[0])}{learned[1..]}. Make sure the device really switched, then teach both again.";
        }
        finally { runtime.Busy = false; }
    }

    private void ActionDone(MethodRecord method)
    {
        if (_setup.EmulatorOf(method) is { } emulator) _setup.Keeper.ActionDone(emulator, _setup.AddressOf(method));
    }

    /// At start-up: wakes the emulator of the first simulation method when the user asked for that, hidden.
    internal void WakeAtStart(DeviceSettings settings)
    {
        if (!settings.Emulator.StartWithWindows || settings.Devices.SelectMany(d => d.Methods).FirstOrDefault(m => m.IsSimulation) is not { } method) return;
        Guard.OffUi("Smart Devices: waking the emulator", async () =>
        {
            await _setup.RefreshAsync();
            if (_setup.EmulatorOf(method) is { } emulator) await _setup.Keeper.WakeAtStartAsync(emulator, _setup.AddressOf(method));
        });
    }

    /// Joins adb to the method's emulator and gives its client; with <paramref name="wake"/> a stopped emulator is started first, with its window
    /// only while the device's set-up is open. The problem as one plain sentence when it cannot.
    private async Task<(AdbClient? Client, string? Problem)> ReachAsync(DeviceRecord device, MethodRecord method, bool wake = false)
    {
        if (_setup.AdbPath is not { } adb) return (null, "adb isn't installed yet.");
        var address = _setup.AddressOf(method);
        if (address.Length == 0) return (null, "No emulator or adb address is set.");
        if (wake && _setup.EmulatorOf(method) is { } emulator
            && await _setup.Keeper.EnsureRunningAsync(emulator, address, show: _runtimeOf(device).InSetup) is { } wakeProblem) return (null, wakeProblem);
        if (await _setup.ConnectAsync(address, null, _ => { }) is { } problem) return (null, problem);
        var client = new AdbClient(adb, address);
        // The one place every run, reading, teaching and recording starts from (wake or not): a screen left asleep or locked answers with a black picture.
        if (!(await client.WakeScreenAsync()).Ok) DeviceService.Log($"Waking the emulator's screen at {address} didn't work.");
        return (client, null);
    }

    /// Starts watching the method's emulator for a recording or a Re-pick; the watcher must be disposed. The problem is one plain sentence.
    public async Task<(SimRecorder? Recorder, string? Problem)> StartRecorderAsync(DeviceRecord device, MethodRecord method)
    {
        if (_runtimeOf(device).Busy) return (null, BusySentence);
        var (client, problem) = await ReachAsync(device, method);
        if (client is null) return (null, problem);
        var recorder = new SimRecorder(client, _setup.AdbPath!, _setup.AddressOf(method));
        if (await recorder.StartAsync() is { } failed)
        {
            recorder.Dispose();
            DeviceService.Log($"\"{DeviceService.DisplayName(device)}\" recording: {failed}");
            return (null, failed);
        }
        return (recorder, null);
    }
}
