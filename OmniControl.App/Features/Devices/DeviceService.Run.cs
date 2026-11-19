namespace OmniControl.App.Features.Devices;

/// How one method's try went: the problem (null when it worked) and, when it worked, the state it left and the words for the status line;
/// a simulation that could not read the state is <c>Unsure</c> (the problem is then its whole sentence).
internal sealed record Attempt(string? Problem, bool? IsOn = null, string Words = "", bool Confirmed = false, string Details = "", int FailedStep = -1,
    string FailedReason = "", bool Unsure = false)
{
    public static Attempt Failed(string problem, string details = "") => new(problem, Details: details);
}

// On, Off and Toggle: the device's ready methods are tried in order and a method that fails
// (unreachable, sign-in, emulator missing...) falls through to the next; the status line names the method that did it and the ones that
// did not. A Toggle that fell through becomes an exact On or Off (the opposite of the last known state), so a method that may have
// switched it before failing is never undone by the next one.
public sealed partial class DeviceService
{
    /// Tells the device to switch, by its methods in order; the result is also in <see cref="DeviceRuntime"/> and the log. Null when it worked, otherwise one plain sentence.
    public Task<string?> RunAsync(DeviceRecord device, DevicePower power) => RunAsync(device, power, null, null);

    /// As <see cref="RunAsync(DeviceRecord, DevicePower)"/> by <paramref name="via"/> only (its "via" action, or its set-up's Test), ready or not.
    public Task<string?> RunAsync(DeviceRecord device, DevicePower power, MethodRecord? via) => RunAsync(device, power, via, null);

    /// Turns the device off, then on again, by this one scripts method with <paramref name="profile"/> (the method's own, or the Advanced pane's
    /// draft). Null when it worked, otherwise one plain sentence; <c>Confirmed</c> when the State command read both changes back.
    public async Task<(string? Problem, bool Confirmed)> TestScriptAsync(DeviceRecord device, MethodRecord method, ScriptProfile? profile = null)
    {
        profile ??= Scripts.ProfileOf(method);
        var problem = await RunAsync(device, DevicePower.Off, method, profile) ?? await RunAsync(device, DevicePower.On, method, profile);
        return (problem, problem is null && RuntimeOf(device).Confirmed);
    }

    /// The run; how it ended is kept on the device's runtime, so a rule step that started it learns the result.
    private async Task<string?> RunAsync(DeviceRecord device, DevicePower power, MethodRecord? via, ScriptProfile? profile)
    {
        var problem = await RunCommandAsync(device, power, via, profile);
        RuntimeOf(device).Commanded(problem);
        if (problem is not null && problem != StoppedSentence) NoteFailed(device, power);
        return problem;
    }

    /// The run itself; a scripts method uses <paramref name="profile"/> instead of its own (the Advanced pane's Test).
    private async Task<string?> RunCommandAsync(DeviceRecord device, DevicePower power, MethodRecord? via, ScriptProfile? profile)
    {
        var runtime = RuntimeOf(device);
        var name = DisplayName(device);
        if (runtime.Busy) return Fail(runtime, name, "it is still busy with the last command.");
        IReadOnlyList<MethodRecord> methods = via is not null ? new[] { via } : ReadyMethods(device);
        if (methods.Count == 0) return Fail(runtime, name, "no method is set up yet.");
        if (!_limiter.TryAcquire()) return Fail(runtime, name, "too many commands in a short time, so this one was skipped.");
        runtime.Busy = true;
        var stop = runtime.BeginRun();
        try
        {
            var failed = new List<(MethodRecord Method, Attempt Attempt)>();
            var goal = power;
            foreach (var method in methods)
            {
                runtime.Trying(method.Id);
                var attempt = MissingFor(method) is { } missing ? Attempt.Failed($"{missing}.") : await TryAsync(device, method, goal, runtime, name, profile, stop);
                if (stop.IsCancellationRequested) return Stopped(runtime, name, method);
                runtime.Tried(method.Id, attempt.Problem);
                if (attempt.Problem is null) return Succeeded(device, runtime, name, method, attempt, failed);
                failed.Add((method, attempt));
                Log($"\"{name}\" via {device.NameOf(method)}: {attempt.Problem.ReplaceLineEndings(" | ")}");
                if (goal == DevicePower.Toggle) goal = runtime.IsOn == true ? DevicePower.Off : DevicePower.On;
            }
            return AllFailed(device, runtime, name, failed);
        }
        finally
        {
            runtime.EndRun();
            runtime.Trying(string.Empty);
            runtime.Busy = false;
        }
    }

    /// What a stopped command returns: not a failure, so no report, no problem on the method and the last known state stays.
    public const string StoppedSentence = "Stopped";

    private string Stopped(DeviceRuntime runtime, string name, MethodRecord method)
    {
        runtime.Tried(method.Id, null);
        runtime.Finished(runtime.IsOn, known: false, $"Last command {DateTime.Now:t} · {StoppedSentence}", failed: false);
        Log($"\"{name}\": stopped by the user");
        return StoppedSentence;
    }

    private Task<Attempt> TryAsync(DeviceRecord device, MethodRecord method, DevicePower power, DeviceRuntime runtime, string name, ScriptProfile? profile, CancellationToken stop) =>
        method.IsSimulation ? SimulationMethod.RunAsync(device, method, power, name, stop) : Scripts.RunAsync(method, power, runtime.IsOn, profile ?? Scripts.ProfileOf(method), stop);

    /// "Simulation: " when the device has more than one method (a one-method device's lines read as they always did).
    private static string Via(DeviceRecord device, MethodRecord method) => device.Methods.Count > 1 ? $"{device.NameOf(method)}: " : string.Empty;

    /// " · Scripts didn't work" for the methods tried before the one that did.
    private static string Others(DeviceRecord device, IReadOnlyList<(MethodRecord Method, Attempt Attempt)> failed) =>
        failed.Count == 0 ? string.Empty : $" · {string.Join(", ", failed.Select(f => device.NameOf(f.Method)))} didn't work";

    private string? Succeeded(DeviceRecord device, DeviceRuntime runtime, string name, MethodRecord method, Attempt attempt, IReadOnlyList<(MethodRecord Method, Attempt Attempt)> failed)
    {
        runtime.Finished(attempt.IsOn, known: true, $"Last command {DateTime.Now:t} · {Via(device, method)}{attempt.Words}{Others(device, failed)}", failed: false,
            details: attempt.Details, confirmed: attempt.Confirmed);
        Log($"\"{name}\" {attempt.Words} via {device.NameOf(method)}");
        if (attempt.IsOn is { } on) NoteState(device, on);
        return null;
    }

    private string AllFailed(DeviceRecord device, DeviceRuntime runtime, string name, IReadOnlyList<(MethodRecord Method, Attempt Attempt)> failed)
    {
        var (last, attempt) = failed[^1];
        if (attempt.Unsure)
        {
            runtime.Finished(null, known: true, $"Last command {DateTime.Now:t} · {Via(device, last)}{attempt.Words}{Others(device, failed.Take(failed.Count - 1).ToList())}", failed: true,
                attempt.FailedStep, attempt.FailedReason, unsure: true, failedMethodId: last.Id);
            _report($"{name}: {attempt.Problem}");
            return $"{name}: {attempt.Problem}";
        }
        var problem = failed.Count == 1 ? attempt.Problem! : $"{string.Join("; ", failed.Select(f => $"{device.NameOf(f.Method)}: {f.Attempt.Problem!.TrimEnd('.')}"))}.";
        var details = string.Join("\n", failed.Select(f => f.Attempt.Details).Where(d => d.Length > 0));
        return Fail(runtime, name, problem, attempt.FailedStep, attempt.FailedReason, details, last.Id);
    }
}
