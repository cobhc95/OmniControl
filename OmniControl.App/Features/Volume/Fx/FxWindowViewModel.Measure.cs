using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Fx;

/// The window's measurement of its whole chain (<see cref="ChainMeasurer"/>): after a <see cref="Timing.FxMeasurePause"/> pause in
/// changes, only while the window is open; a new change cancels a running one (the next starts once it let go of its plug-ins), and
/// an unchanged chain is not measured again.
public sealed partial class FxWindowViewModel
{
    private readonly DispatcherTimer _measurePause = new() { Interval = Timing.FxMeasurePause };
    private readonly ChainMeasurer _measurer = new();
    private CancellationTokenSource? _measuring;
    private string? _measuredSignature;

    /// Something the measurement depends on may have changed: measure again after the pause.
    private void MeasureSoon()
    {
        _measuring?.Cancel();
        _measurePause.Stop();
        _measurePause.Start();
    }

    private void OnMeasurePause(object? sender, EventArgs e)
    {
        _measurePause.Stop();
        Guard.Fire("FX window: measuring the chain", MeasureAsync);
    }

    private async Task MeasureAsync()
    {
        if (_engine.MeasureJobOf(_key) is not { } job)
        {
            _measuredSignature = null;
            Analysis.ShowMeasured(null);
            return;
        }
        if (job.Signature == _measuredSignature)
        {
            Analysis.SetMeasuring(false);
            return;
        }
        using var cancel = new CancellationTokenSource();
        _measuring = cancel;
        Analysis.SetMeasuring(true);
        var measured = await _measurer.RunAsync(job, FxAnalysisViewModel.Freqs, cancel.Token);
        if (ReferenceEquals(_measuring, cancel)) _measuring = null;
        if (cancel.IsCancellationRequested) return; // a newer change measures again
        _measuredSignature = measured is null ? null : job.Signature;
        Analysis.ShowMeasured(measured);
    }

    private void StopMeasuring()
    {
        _measurePause.Stop();
        _measurePause.Tick -= OnMeasurePause;
        _measuring?.Cancel();
    }
}
