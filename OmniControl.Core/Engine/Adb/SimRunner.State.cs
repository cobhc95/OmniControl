using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>A look at the device's card: what the reading says, and the switch it looked at (null when the card was not found).</summary>
public sealed record StateLook(StateReading Reading, SimNode? Switch);

// Teaching a device's on and off, reading its state, and the On / Off / Toggle run that taps only when the reading says so.
public static partial class SimRunner
{
    /// <summary>
    /// Switches the device: the steps up to the switch, a reading, the tap when it is needed, a second reading that must show the change, then
    /// the steps after the switch. See the class summary for the rules.
    /// </summary>
    public static async Task<SimResult> RunAsync(IAdbScreen adb, IReadOnlyList<SimStep> steps, StateModel model, SimGoal goal, SimTiming timing, CancellationToken stop = default)
    {
        var at = SwitchIndex(steps);
        if (at < 0) return Failed(-1, "no step taps the device's switch");
        if (!model.IsTaught) return Failed(at, "its on and off are not taught yet");
        for (var i = 0; i < at; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;

        var before = await FindAndReadAsync(adb, model, timing, stop);
        if (before.Switch is not { } node) return Failed(at, $"{Words(steps[at])} wasn't found on the screen");
        var was = before.Reading.State;
        if (goal != SimGoal.Toggle && was == DeviceState.Unknown)
            return new SimResult(at, "its state couldn't be read", null, Unsure: true, Confidence: before.Reading.Confidence);
        if (goal != SimGoal.Toggle && (was == DeviceState.On) == (goal == SimGoal.On))
            return new SimResult(-1, null, was == DeviceState.On, Skipped: true, Confidence: before.Reading.Confidence);

        if (!(await adb.TapAsync(node.CenterX, node.CenterY)).Ok) return Failed(at, "adb couldn't tap");
        await Task.Delay(timing.Settle, stop);
        var after = await WaitChangeAsync(adb, model, was, timing, stop);
        var confidence = Math.Min(before.Reading.Confidence, after.Reading.Confidence);
        if (was == DeviceState.Unknown || after.Reading.State == DeviceState.Unknown)
            return new SimResult(at, "its state couldn't be read, so the change isn't confirmed", null, Unsure: true, Confidence: confidence);
        if (after.Reading.State == was)
            return new SimResult(at, $"the tap didn't change it (still {(was == DeviceState.On ? "on" : "off")})", was == DeviceState.On, Confidence: confidence);

        for (var i = at + 1; i < steps.Count; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;
        return new SimResult(-1, null, after.Reading.State == DeviceState.On, Confidence: confidence);
    }

    /// <summary>
    /// Only reads the state: the steps up to the switch, then a reading; nothing is tapped. <see cref="SimResult.Unsure"/> when the reading was
    /// not clear; a failed step or a switch that was not found comes back as the problem.
    /// </summary>
    public static async Task<SimResult> ReadStateAsync(IAdbScreen adb, IReadOnlyList<SimStep> steps, StateModel model, SimTiming timing, CancellationToken stop = default)
    {
        var at = SwitchIndex(steps);
        if (at < 0) return Failed(-1, "no step taps the device's switch");
        if (!model.IsTaught) return Failed(at, "its on and off are not taught yet");
        for (var i = 0; i < at; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;
        var look = await FindAndReadAsync(adb, model, timing, stop);
        if (look.Switch is null) return Failed(at, $"{Words(steps[at])} wasn't found on the screen");
        if (look.Reading.State == DeviceState.Unknown) return new SimResult(at, "its state couldn't be read", null, Unsure: true, Confidence: look.Reading.Confidence);
        return new SimResult(-1, null, look.Reading.State == DeviceState.On, Confidence: look.Reading.Confidence);
    }

    /// <summary>
    /// Takes <paramref name="count"/> looks at the switch's card, <paramref name="gap"/> apart, while the person holds the device in one state
    /// (teaching). The switch is the step's element, or where the model last found it. A short phrase when the screen or the switch was not there.
    /// </summary>
    public static async Task<(IReadOnlyList<StateSample> Samples, string? Problem)> CaptureAsync(IAdbScreen adb, SimStep switchStep, StateModel model, int count, TimeSpan gap, CancellationToken stop = default)
    {
        var samples = new List<StateSample>();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) await Task.Delay(gap, stop);
            if (await ReadAsync(adb) is not { } screen) return (samples, "the screen couldn't be read from adb");
            if (FindSwitch(screen, switchStep, model) is not { } node) return (samples, $"{Words(switchStep)} isn't on the emulator's screen");
            samples.Add(StateSample.Take(screen, node, Png.Decode(await adb.ScreenshotAsync())));
        }
        return (samples, null);
    }

    /// <summary>The switch while teaching: the step's own element, else the taught target, else the element with the step's id (its words may have changed with the state).</summary>
    private static SimNode? FindSwitch(SimScreen screen, SimStep step, StateModel model)
    {
        if (screen.Find(step) is { } found) return found;
        if (model.Target?.Locate(screen) is { } taught) return taught;
        if (step.ResourceId.Length == 0) return null;
        var byId = step.Clone();
        (byId.Text, byId.Description) = (string.Empty, string.Empty);
        return screen.Find(byId);
    }

    /// <summary>Reads the state on the screen as it is: waits for the card, then scrolls up and down to find it.</summary>
    private static async Task<StateLook> FindAndReadAsync(IAdbScreen adb, StateModel model, SimTiming timing, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (await LookAsync(adb, model, stop) is { Switch: not null } look) return look;
            await Task.Delay(timing.Poll, stop);
        } while (clock.Elapsed < timing.ElementWait);

        // The card may have scrolled away: look further down the list, then back up past where it started.
        foreach (var direction in Enumerable.Repeat("up", timing.ScrollTries).Concat(Enumerable.Repeat("down", timing.ScrollTries * 2)))
        {
            if (!(await SwipeAsync(adb, direction)).Ok) break;
            await Task.Delay(timing.Settle, stop);
            if (await LookAsync(adb, model, stop) is { Switch: not null } look) return look;
        }
        return new StateLook(new StateReading(DeviceState.Unknown, 0), null);
    }

    /// <summary>Reads again until the state is known and differs from <paramref name="was"/>, or the wait is over (then the last look).</summary>
    private static async Task<StateLook> WaitChangeAsync(IAdbScreen adb, StateModel model, DeviceState was, SimTiming timing, CancellationToken stop)
    {
        var last = new StateLook(new StateReading(DeviceState.Unknown, 0), null);
        var clock = Stopwatch.StartNew();
        do
        {
            last = await LookAsync(adb, model, stop);
            if (last.Reading.State != DeviceState.Unknown && last.Reading.State != was) return last;
            await Task.Delay(timing.Poll, stop);
        } while (clock.Elapsed < timing.ElementWait);
        return last;
    }

    /// <summary>One look: the dump, the switch in it and, when found, a screenshot and the reading.</summary>
    private static async Task<StateLook> LookAsync(IAdbScreen adb, StateModel model, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        if (await ReadAsync(adb) is not { } screen || model.Target?.Locate(screen) is not { } node) return new StateLook(new StateReading(DeviceState.Unknown, 0), null);
        var sample = StateSample.Take(screen, node, Png.Decode(await adb.ScreenshotAsync()));
        return new StateLook(model.Read(sample), node);
    }
}
