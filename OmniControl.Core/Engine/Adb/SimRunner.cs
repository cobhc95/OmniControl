using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>How long a step waits for what it needs (given by the app, so the values live in one place).</summary>
public sealed record SimTiming(TimeSpan ElementWait, TimeSpan Poll, TimeSpan Settle);

/// <summary>What a run is for: a state to reach, or a toggle (which always taps).</summary>
public enum SimGoal { On, Off, Toggle }

/// <summary>How a run ended: <see cref="FailedIndex"/> is the step that stopped it (-1 = none), <see cref="Problem"/> a short phrase, <see cref="IsOn"/> the state the Check step showed, when there was one, and <see cref="Skipped"/> that the screen already showed the wanted state, so nothing was tapped.</summary>
public sealed record SimResult(int FailedIndex, string? Problem, bool? IsOn, bool Skipped = false)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Plays the recorded steps on an Android screen. A step runs only after the one before it worked; the first that fails stops the run.
/// When the steps end with a Check ("screen shows On") and have a Tap before it, that Tap is the switch: the state is read on the screen
/// that holds the switch, On or Off skips the tap when the screen already shows that state, Toggle always taps, and the Check afterwards
/// must show the new state.
/// </summary>
public static class SimRunner
{
    public static async Task<SimResult> RunAsync(IAdbScreen adb, IReadOnlyList<SimStep> steps, SimGoal goal, SimTiming timing, CancellationToken stop = default)
    {
        var check = steps.Count > 0 && steps[^1].Kind == SimStepKind.Check ? steps.Count - 1 : -1;
        var tap = check < 0 ? -1 : LastTapBefore(steps, check);
        if (tap < 0) return await RunPlainAsync(adb, steps, timing, stop);

        for (var i = 0; i < tap; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;

        var (screen, problem) = await WaitForAsync(adb, steps[tap], timing, stop);
        if (screen is null) return Failed(tap, problem);
        var showsOn = screen.Shows(CheckWords(steps[check]));
        var wantOn = goal switch { SimGoal.On => true, SimGoal.Off => false, _ => !showsOn };
        if (showsOn == wantOn) return new SimResult(-1, null, showsOn, Skipped: true);

        for (var i = tap; i < check; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;

        var after = await WaitStateAsync(adb, steps[check], wantOn, timing, stop);
        return after is null
            ? Failed(check, $"the screen didn't {(wantOn ? "show" : "stop showing")} \"{CheckWords(steps[check])}\" afterwards")
            : new SimResult(-1, null, wantOn);
    }

    /// <summary>Runs one step on its own (the step editor's Test step); a Check needs its text on the screen.</summary>
    public static async Task<SimResult> TestStepAsync(IAdbScreen adb, SimStep step, int index, SimTiming timing, CancellationToken stop = default) =>
        await StepAsync(adb, step, index, timing, stop) ?? new SimResult(-1, null, null);

    private static async Task<SimResult> RunPlainAsync(IAdbScreen adb, IReadOnlyList<SimStep> steps, SimTiming timing, CancellationToken stop)
    {
        for (var i = 0; i < steps.Count; i++)
            if (await StepAsync(adb, steps[i], i, timing, stop) is { } failed) return failed;
        return new SimResult(-1, null, null);
    }

    private static int LastTapBefore(IReadOnlyList<SimStep> steps, int end)
    {
        for (var i = end - 1; i >= 0; i--)
            if (steps[i].Kind == SimStepKind.Tap) return i;
        return -1;
    }

    private static string CheckWords(SimStep check) => check.Text.Length > 0 ? check.Text : check.Description;

    private static SimResult Failed(int index, string? problem) => new(index, problem ?? "it failed", null);

    /// <summary>Does one step; null when it worked, otherwise the result that stops the run.</summary>
    private static async Task<SimResult?> StepAsync(IAdbScreen adb, SimStep step, int index, SimTiming timing, CancellationToken stop)
    {
        if (SimSteps.Missing(step) is { } missing) return Failed(index, missing);
        stop.ThrowIfCancellationRequested();
        AdbResult result;
        switch (step.Kind)
        {
            case SimStepKind.OpenApp:
                result = await adb.OpenAppAsync(step.Package);
                if (!result.Ok) return Failed(index, $"adb couldn't open {step.Package}");
                break;
            case SimStepKind.WaitFor:
            {
                var (found, problem) = await WaitForAsync(adb, step, timing, stop);
                return found is null ? Failed(index, problem) : null;
            }
            case SimStepKind.Tap:
            {
                var (found, problem) = await WaitForAsync(adb, step, timing, stop);
                if (found is null) return Failed(index, problem);
                var (x, y) = PointOf(found, step);
                result = await adb.TapAsync(x, y);
                if (!result.Ok) return Failed(index, "adb couldn't tap");
                break;
            }
            case SimStepKind.TypeText:
                result = await adb.TypeAsync(step.Text);
                if (!result.Ok) return Failed(index, "adb couldn't type");
                break;
            case SimStepKind.Swipe:
                result = await SwipeAsync(adb, step.Direction);
                if (!result.Ok) return Failed(index, "adb couldn't swipe");
                break;
            case SimStepKind.Back:
                result = await adb.BackAsync();
                if (!result.Ok) return Failed(index, "adb couldn't press Back");
                break;
            default:
                return await WaitStateAsync(adb, step, true, timing, stop) is null ? Failed(index, $"the screen doesn't show \"{CheckWords(step)}\"") : null;
        }
        await Task.Delay(timing.Settle, stop);
        return null;
    }

    private static (int X, int Y) PointOf(SimScreen screen, SimStep step) =>
        screen.Find(step) is { } node ? (node.CenterX, node.CenterY) : (step.X, step.Y);

    /// <summary>
    /// Waits until the element is on the screen and gives that screen, or says why not. A step with no text, description or id has nothing
    /// to wait for: it is given the current screen and taps its position.
    /// </summary>
    private static async Task<(SimScreen? Screen, string? Problem)> WaitForAsync(IAdbScreen adb, SimStep step, SimTiming timing, CancellationToken stop)
    {
        if (!step.HasIdentity) return (await ReadAsync(adb) ?? SimScreen.Parse("<hierarchy></hierarchy>"), null);
        var clock = Stopwatch.StartNew();
        string problem;
        do
        {
            stop.ThrowIfCancellationRequested();
            var screen = await ReadAsync(adb);
            if (screen is null) problem = "the screen couldn't be read from adb";
            else if (screen.Find(step) is not null) return (screen, null);
            else problem = $"{Words(step)} wasn't found on the screen";
            await Task.Delay(timing.Poll, stop);
        } while (clock.Elapsed < timing.ElementWait);
        return (null, problem);
    }

    private static string Words(SimStep step) => SimSteps.Words(step).Target is { Length: > 0 } target ? target : "the element";

    /// <summary>Waits until the check text is on the screen (<paramref name="shown"/>) or off it; null when that did not happen in time.</summary>
    private static async Task<SimScreen?> WaitStateAsync(IAdbScreen adb, SimStep check, bool shown, SimTiming timing, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            stop.ThrowIfCancellationRequested();
            var screen = await ReadAsync(adb);
            if (screen is not null && screen.Shows(CheckWords(check)) == shown) return screen;
            await Task.Delay(timing.Poll, stop);
        } while (clock.Elapsed < timing.ElementWait);
        return null;
    }

    private static async Task<SimScreen?> ReadAsync(IAdbScreen adb)
    {
        var dump = await adb.DumpAsync();
        return dump.Ok ? SimScreen.Parse(dump.Text) : null;
    }

    private static async Task<AdbResult> SwipeAsync(IAdbScreen adb, string direction)
    {
        var size = await adb.SizeAsync();
        if (!size.Ok || AdbClient.SizeOf(size.Text) is not { } screen) return new AdbResult(false, "no screen size");
        int cx = screen.Width / 2, cy = screen.Height / 2, dx = screen.Width / 3, dy = screen.Height / 3;
        // Swiping "up" moves the finger up, which scrolls the content down.
        return direction switch
        {
            "up" => await adb.SwipeAsync(cx, cy + dy, cx, cy - dy),
            "down" => await adb.SwipeAsync(cx, cy - dy, cx, cy + dy),
            "left" => await adb.SwipeAsync(cx + dx, cy, cx - dx, cy),
            _ => await adb.SwipeAsync(cx - dx, cy, cx + dx, cy),
        };
    }
}
