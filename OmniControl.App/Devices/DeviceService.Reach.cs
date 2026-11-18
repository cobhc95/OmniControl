using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;

namespace OmniControl.App.Devices;

// What a device's master actions tell a rule that runs them (Hotkeys/ActionReach): the device needs the network; it answers when its
// Scripts method reads its state (a simulation-only device is never woken just to look, so for it the network check is all); and a run
// started at a given time ended as its runtime says (DeviceRuntime.Commanded), or did nothing because it was already there.
public sealed partial class DeviceService
{
    private ActionReach ReachOf(DeviceRecord device, DevicePower power) =>
        new(DisplayName(device), NeedsNetwork: true, _ => ProbeAsync(device), (since, stop) => OutcomeSinceAsync(device, power, since, stop));

    /// <summary>Null when the device answers now (or cannot be asked without waking an emulator); otherwise why not. Read on the UI thread, like every check.</summary>
    private async Task<string?> ProbeAsync(DeviceRecord device)
    {
        var runtime = RuntimeOf(device);
        if (runtime.Busy) return "it is busy with another command";
        if (!CanCheckByScript(device)) return null;
        return await Dispatcher.UIThread.InvokeAsync(() => CheckStateAsync(device, scriptsOnly: true));
    }

    /// <summary>
    /// Waits for the command started at <paramref name="since"/> to end and says how it went. When nothing has run for
    /// <see cref="Timing.StepOutcomeIdle"/> the run path skipped it: done when the device is already in the wanted state, else a problem.
    /// </summary>
    private async Task<StepOutcome> OutcomeSinceAsync(DeviceRecord device, DevicePower power, DateTime since, CancellationToken stop)
    {
        var runtime = RuntimeOf(device);
        var name = DisplayName(device);
        var idle = TimeSpan.Zero;
        for (var waited = TimeSpan.Zero; waited < Timing.StepOutcomeWait; waited += Timing.StepOutcomePoll)
        {
            if (runtime.CommandEnded is { } ended && ended >= since)
                return runtime.CommandProblem is not { } problem ? StepOutcome.Done : new StepOutcome(problem, MayRetry: problem != StoppedSentence);
            idle = runtime.Busy ? TimeSpan.Zero : idle + Timing.StepOutcomePoll;
            if (idle >= Timing.StepOutcomeIdle)
                return Wanted(power) is { } on && runtime.IsOn == on ? StepOutcome.Done : new StepOutcome($"{name} was not switched: the command did not run");
            await Task.Delay(Timing.StepOutcomePoll, stop);
        }
        return new StepOutcome($"{name} did not finish within {Timing.StepOutcomeWait.TotalMinutes:0} minutes");
    }

    /// <summary>The state On or Off asks for; null for Toggle (it depends on the state now).</summary>
    private static bool? Wanted(DevicePower power) => power switch
    {
        DevicePower.On => true,
        DevicePower.Off => false,
        _ => null,
    };
}
