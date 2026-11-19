namespace OmniControl.App.Features.Devices;

// Reading a device's real state without switching it: the ready methods are asked in order and
// the first that reads wins. Scripts run the template's State command; simulation wakes the
// emulator hidden and reads the taught state off the app's screen. Nothing is tapped or switched. When it runs on its own is DeviceAutoChecks'.
public sealed partial class DeviceService
{
    /// Reads the state now and updates what is known; the result is also in <see cref="DeviceRuntime"/> and the log. Null when it worked,
    /// otherwise one plain sentence. <paramref name="scriptsOnly"/>: only the Scripts methods are asked (the tray panel's live state never
    /// wakes an emulator).
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
                    : method.IsSimulation ? await SimulationMethod.CheckAsync(device, method, name) : await Scripts.CheckAsync(method);
                if (attempt.Problem is null)
                {
                    var on = attempt.IsOn == true;
                    var before = runtime.IsOn;
                    runtime.Finished(on, known: true, $"Checked {DateTime.Now:t} · {Via(device, method)}reads {(on ? "on" : "off")}{Others(device, failed)}", failed: false,
                        details: attempt.Details, confirmed: true);
                    Log($"\"{name}\" check via {device.NameOf(method)}: reads {(on ? "on" : "off")}");
                    if (before is { } was && was != on) NoteState(device, on); // a first read, or the same state again, is not news
                    return null;
                }
                failed.Add((method, attempt));
            }
            var (last, lastAttempt) = failed[^1];
            if (lastAttempt.Unsure)
            {
                runtime.Finished(null, known: true, $"Checked {DateTime.Now:t} · state unknown", failed: true, lastAttempt.FailedStep, lastAttempt.FailedReason, unsure: true,
                    failedMethodId: last.Id);
                return $"{name}: {SimulationMethod.ReTeachSentence}";
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

    /// The smart devices' entry in the master list of hardware watches.
    public const string HardwareWatchId = "devices.smart";

    /// One automatic read: ready, set up and Scripts only; a problem is in the device's foot line and the log, never a pop-up.
    private async Task CheckAutoAsync(DeviceRecord device)
    {
        if (device.IsReady && CanCheckByScript(device) && MissingFor(device) is null && !RuntimeOf(device).InSetup) await CheckStateAsync(device, scriptsOnly: true);
    }

    /// A Scripts method is ready, so the state can be read quickly without waking an emulator.
    public bool CanCheckByScript(DeviceRecord device) => ReadyMethods(device).Any(m => !m.IsSimulation);

    /// A failed read: one plain line (Copy details gives the log lines); the last known state stays. Not reported on screen, since checks also run on their own.
    private string CheckFailed(DeviceRuntime runtime, string name, string problem, string details = "")
    {
        runtime.Finished(runtime.IsOn, known: false, $"Check failed: {problem}", failed: true, details: details.Length > 0 ? details : problem);
        Log($"\"{name}\" check: {problem.ReplaceLineEndings(" | ")}");
        return $"{name} couldn't be checked: {problem}";
    }
}
