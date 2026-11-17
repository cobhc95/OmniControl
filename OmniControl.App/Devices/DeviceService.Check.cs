using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

// Reading a device's real state without switching it (docs/mockups/device-state-refresh.html): the ready methods are asked in order and
// the first that reads wins (docs/mockups/device-multiple-methods.html). Scripts run the template's State command; simulation wakes the
// emulator hidden and reads the taught state off the app's screen. Nothing is tapped or switched.
public sealed partial class DeviceService
{
    /// <summary>
    /// Reads the state now and updates what is known; the result is also in <see cref="DeviceRuntime"/> and the log. Null when it worked,
    /// otherwise one plain sentence. <paramref name="scriptsOnly"/>: only the Scripts methods are asked (the tray panel's live state never
    /// wakes an emulator).
    /// </summary>
    public async Task<string?> CheckStateAsync(DeviceRecord device, bool scriptsOnly = false)
    {
        var runtime = RuntimeOf(device);
        var name = DisplayName(device);
        if (runtime.Busy) return null; // a command is running and will say the state itself
        var methods = ReadyMethods(device).Where(m => !scriptsOnly || !m.IsSimulation).ToList();
        if (methods.Count == 0) return CheckFailed(runtime, name, "no method is set up yet.");
        runtime.Checking = true;
        runtime.Busy = true;
        try
        {
            var failed = new List<(MethodRecord Method, Attempt Attempt)>();
            foreach (var method in methods)
            {
                var attempt = MissingFor(method) is { } missing ? Attempt.Failed($"{missing}.")
                    : method.IsSimulation ? await CheckSimulationAsync(device, method, name) : await CheckScriptAsync(method);
                if (attempt.Problem is null)
                {
                    var on = attempt.IsOn == true;
                    runtime.Finished(on, known: true, $"Checked {DateTime.Now:t} · {Via(device, method)}reads {(on ? "on" : "off")}{Others(device, failed)}", failed: false,
                        details: attempt.Details, confirmed: true);
                    Log($"\"{name}\" check via {device.NameOf(method)}: reads {(on ? "on" : "off")}");
                    return null;
                }
                failed.Add((method, attempt));
            }
            var (last, lastAttempt) = failed[^1];
            if (lastAttempt.Unsure)
            {
                runtime.Finished(null, known: true, $"Checked {DateTime.Now:t} · state unknown", failed: true, lastAttempt.FailedStep, lastAttempt.FailedReason, unsure: true,
                    failedMethodId: last.Id);
                return $"{name}: {ReTeachSentence}";
            }
            var problem = failed.Count == 1 ? lastAttempt.Problem! : $"{string.Join("; ", failed.Select(f => $"{device.NameOf(f.Method)}: {f.Attempt.Problem!.TrimEnd('.')}"))}.";
            return CheckFailed(runtime, name, problem, string.Join("\n", failed.Select(f => f.Attempt.Details).Where(d => d.Length > 0)));
        }
        finally
        {
            runtime.Checking = false;
            runtime.Busy = false;
        }
    }

    // ---- Reading on its own (docs/mockups/device-autocheck.html): from the service, so the tray tiles show real states without the page ----

    private DispatcherTimer? _autoTimer;
    private readonly Dictionary<string, DateTime> _lastAuto = new();
    private bool _autoRunning;

    /// <summary>The device's auto-check changed (a pick or the switch): the page's chips read it again.</summary>
    public event Action? AutoCheckChanged;

    /// <summary>Starts the start-up check (a few seconds after the window is up) and the timer that keeps the periodic ones; once. Never in a screenshot export.</summary>
    public void StartAutoChecks()
    {
        if (StartupMode.Inert || _autoTimer is not null) return;
        _autoTimer = new DispatcherTimer { Interval = Timing.DeviceAutoCheckTick };
        _autoTimer.Tick += (_, _) => _ = RunAutoChecksAsync(atStart: false);
        _autoTimer.Start();
        DispatcherTimer.RunOnce(() => _ = RunAutoChecksAsync(atStart: true), Timing.DeviceStartCheckDelay);
    }

    /// <summary>The choice a device follows now (null = manual only).</summary>
    public AutoCheck? AutoCheckOf(DeviceRecord device) => AutoChecks.Effective(Settings, device);

    public void PickAutoCheck(DeviceRecord device, AutoCheck value)
    {
        AutoChecks.Pick(Settings, device, value);
        AutoCheckEdited();
    }

    public bool AutoCheckOnTrayOf(DeviceRecord device) => AutoChecks.OnTray(Settings, device);

    public void PickAutoCheckOnTray(DeviceRecord device, bool value)
    {
        AutoChecks.PickOnTray(Settings, device, value);
        AutoCheckEdited();
    }

    public void SetAutoCheckSame(DeviceRecord device, bool same)
    {
        AutoChecks.SetSame(Settings, device, same);
        AutoCheckEdited();
    }

    private void AutoCheckEdited()
    {
        Edited();
        AutoCheckChanged?.Invoke();
    }

    /// <summary>The page opened: devices set to "When the page or tray panel opens" read their state (Scripts only).</summary>
    public async Task CheckWhenShownAsync()
    {
        foreach (var device in Settings.Devices.ToList())
            if (AutoCheckOf(device) is { } choice && AutoChecks.Of(choice).WhenShown) await CheckAutoAsync(device);
    }

    private async Task RunAutoChecksAsync(bool atStart)
    {
        if (_autoRunning) return;
        _autoRunning = true;
        try
        {
            if (atStart) await Setup.RefreshAsync(); // Python and python-kasa are looked for before the first read
            var now = DateTime.UtcNow;
            foreach (var device in Settings.Devices.ToList())
            {
                if (AutoCheckOf(device) is not { } value) continue;
                var choice = AutoChecks.Of(value);
                var due = atStart ? choice.AtStart : choice.Every is { } every && AutoChecks.Due(_lastAuto.TryGetValue(device.Id, out var last) ? last : null, now, every);
                if (!due) continue;
                _lastAuto[device.Id] = now;
                await CheckAutoAsync(device);
            }
        }
        finally { _autoRunning = false; }
    }

    /// <summary>One automatic read: ready, set up and Scripts only; a problem is in the device's foot line and the log, never a pop-up.</summary>
    private async Task CheckAutoAsync(DeviceRecord device)
    {
        if (device.IsReady && CanCheckByScript(device) && MissingFor(device) is null && !RuntimeOf(device).InSetup) await CheckStateAsync(device, scriptsOnly: true);
    }

    /// <summary>A Scripts method is ready, so the state can be read quickly without waking an emulator.</summary>
    public bool CanCheckByScript(DeviceRecord device) => ReadyMethods(device).Any(m => !m.IsSimulation);

    private async Task<Attempt> CheckScriptAsync(MethodRecord method)
    {
        var profile = ProfileOf(method);
        if (profile.State.Trim().Length == 0) return Attempt.Failed("its template has no State command to read with.");
        var run = await RunTemplateAsync(method, profile.State, profile);
        if (run.Problem is not null) return Attempt.Failed(run.Problem, run.Details);
        if (KasaCommand.StateOf(run.Printed) is not { } on) return Attempt.Failed("its answer didn't say whether it is on or off.", run.Details);
        return new Attempt(null, on, Details: run.Details);
    }

    private async Task<Attempt> CheckSimulationAsync(DeviceRecord device, MethodRecord method, string name)
    {
        var (client, problem) = await ReachAsync(device, method, wake: true);
        if (client is null)
        {
            EmulatorActionDone(method);
            return Attempt.Failed(problem!);
        }
        try
        {
            using var limit = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
            var result = await SimRunner.ReadStateAsync(client, method.Steps.ToList(), method.Taught, Pace, limit.Token);
            method.Taught.Note(result.Confidence);
            Edited();
            if (result.Unsure)
            {
                Log($"\"{name}\" check: {result.Problem} (confidence {result.Confidence:0.00})");
                return new Attempt(ReTeachSentence, FailedStep: result.FailedIndex, FailedReason: result.Problem ?? string.Empty, Unsure: true);
            }
            return result.Ok ? new Attempt(null, result.IsOn == true) : Attempt.Failed($"step {result.FailedIndex + 1}: {result.Problem}.");
        }
        catch (OperationCanceledException)
        {
            return Attempt.Failed($"the check took longer than {Timing.DeviceSimRunTimeout.TotalMinutes:0} minutes, so it was stopped.");
        }
        finally { EmulatorActionDone(method); }
    }

    /// <summary>A failed read: one plain line (Copy details gives the log lines); the last known state stays. Not reported on screen, since checks also run on their own.</summary>
    private string CheckFailed(DeviceRuntime runtime, string name, string problem, string details = "")
    {
        runtime.Finished(runtime.IsOn, known: false, $"Check failed: {problem}", failed: true, details: details.Length > 0 ? details : problem);
        Log($"\"{name}\" check: {problem.ReplaceLineEndings(" | ")}");
        return $"{name} couldn't be checked: {problem}";
    }
}
