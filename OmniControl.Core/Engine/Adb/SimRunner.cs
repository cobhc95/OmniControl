using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>How long a step waits for what it needs (given by the app, so the values live in one place).</summary>
public sealed record SimTiming(TimeSpan ElementWait, TimeSpan Poll, TimeSpan Settle, int ScrollTries = 0, Action<string>? Log = null);

/// <summary>What a run is for: a state to reach, or a toggle (which always taps).</summary>
public enum SimGoal { On, Off, Toggle }

/// <summary>
/// How a run ended: <see cref="FailedIndex"/> is the step that stopped it (-1 = none), <see cref="Problem"/> a short phrase, <see cref="IsOn"/> the
/// state read at the end, <see cref="Skipped"/> that the device already was in the wanted state so nothing was tapped, <see cref="Unsure"/> that
/// its state could not be read (so On and Off tapped nothing), and <see cref="Confidence"/> how sure the readings were (0-1).
/// </summary>
public sealed record SimResult(int FailedIndex, string? Problem, bool? IsOn, bool Skipped = false, bool Unsure = false, double Confidence = 0)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Plays the recorded steps on an Android screen. A step runs only after the one before it worked; the first that fails stops the run.
/// The last Tap is the device's switch (<see cref="SwitchIndex"/>): the steps before it lead to the device's card, then the taught
/// <see cref="StateModel"/> reads the state there. On and Off tap only when the state is the other one, Toggle always taps; after the tap the
/// state is read again and the run counts as working only when it changed. A state that cannot be read means On and Off tap nothing.
/// </summary>
public static partial class SimRunner
{
    /// <summary>The step that is the device's switch: the last Tap; -1 when the steps have none.</summary>
    public static int SwitchIndex(IReadOnlyList<SimStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
            if (steps[i].Kind == SimStepKind.Tap) return i;
        return -1;
    }

    /// <summary>Runs one step on its own (the step editor's Test step); a Check needs its text on the screen.</summary>
    public static async Task<SimResult> TestStepAsync(IAdbScreen adb, SimStep step, int index, SimTiming timing, CancellationToken stop = default) =>
        await StepAsync(adb, step, index, timing, stop) ?? new SimResult(-1, null, null);

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
                return await WaitShowsAsync(adb, step, timing, stop) ? null : Failed(index, $"the screen doesn't show \"{(step.Text.Length > 0 ? step.Text : step.Description)}\"");
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

        // Not there yet: the page may be longer than the screen, so scroll it a few times and look again.
        for (var i = 0; i < timing.ScrollTries; i++)
        {
            stop.ThrowIfCancellationRequested();
            if (!(await SwipeAsync(adb, "up")).Ok) break;
            await Task.Delay(timing.Settle, stop);
            if (await ReadAsync(adb) is { } screen && screen.Find(step) is not null) return (screen, null);
        }
        return (null, problem);
    }

    private static string Words(SimStep step) => SimSteps.Words(step).Target is { Length: > 0 } target ? target : "the element";

    /// <summary>Waits until a Check's words are on the screen; false when they did not show in time.</summary>
    private static async Task<bool> WaitShowsAsync(IAdbScreen adb, SimStep check, SimTiming timing, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            stop.ThrowIfCancellationRequested();
            if (await ReadAsync(adb) is { } screen && screen.Shows(check.Text.Length > 0 ? check.Text : check.Description)) return true;
            await Task.Delay(timing.Poll, stop);
        } while (clock.Elapsed < timing.ElementWait);
        return false;
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
