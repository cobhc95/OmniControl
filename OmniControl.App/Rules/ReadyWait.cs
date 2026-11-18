using OmniControl.App.Hotkeys;

namespace OmniControl.App.Rules;

/// <summary>How a readiness wait ended: ready or not, after how many checks, and the last reason it was not.</summary>
public sealed record ReadyResult(bool Ready, int Checks, string? Problem);

/// <summary>
/// The two waits of a step that needs the network or a device (<see cref="ActionReach"/>): checking until it answers, with growing waits in
/// between and a time limit, and trying a failed step again a few times. Pure: the waiting itself is passed in, so tests run without delays.
/// </summary>
public static class ReadyWait
{
    /// <summary>The wait before check or try number <paramref name="index"/> (0-based) from a schedule whose last value repeats.</summary>
    public static TimeSpan Step(IReadOnlyList<TimeSpan> schedule, int index) => schedule[Math.Min(index, schedule.Count - 1)];

    /// <summary>
    /// Asks <paramref name="probe"/> until it answers null (ready), waiting <paramref name="backoff"/> between checks, and gives up once the
    /// waits add up to <paramref name="timeout"/>. A stop (rules paused) ends it with an <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<ReadyResult> UntilAsync(Func<CancellationToken, Task<string?>> probe, IReadOnlyList<TimeSpan> backoff, TimeSpan timeout,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken stop)
    {
        var waited = TimeSpan.Zero;
        for (var check = 1; ; check++)
        {
            stop.ThrowIfCancellationRequested();
            var problem = await probe(stop);
            if (problem is null) return new ReadyResult(true, check, null);
            var next = Step(backoff, check - 1);
            if (waited + next > timeout) return new ReadyResult(false, check, problem);
            await delay(next, stop);
            waited += next;
        }
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> (given the try number, from 1) until it works, says trying again would not help, or
    /// <paramref name="maxTries"/> were made, waiting <paramref name="backoff"/> between tries. The last outcome and how many tries were made.
    /// </summary>
    public static async Task<(StepOutcome Outcome, int Tries)> RetryAsync(Func<int, Task<StepOutcome>> attempt, IReadOnlyList<TimeSpan> backoff, int maxTries,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken stop)
    {
        var outcome = StepOutcome.Done;
        for (var tryNumber = 1; tryNumber <= maxTries; tryNumber++)
        {
            stop.ThrowIfCancellationRequested();
            outcome = await attempt(tryNumber);
            if (outcome.Problem is null || !outcome.MayRetry || tryNumber == maxTries) return (outcome, tryNumber);
            await delay(Step(backoff, tryNumber - 1), stop);
        }
        return (outcome, maxTries);
    }
}
