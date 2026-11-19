using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// Whether a Volume control works now (from a <see cref="Gate"/> the page works out): when it does not, the control stays visible but
/// dimmed with the reason as tooltip.
public sealed partial class ControlGate : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAvailable))] private string? _reason;

    /// The reason as one short sentence for a small pop-down (the gate's note, else its reason); null = the control works.
    [ObservableProperty] private string? _note;

    public bool IsAvailable => Reason is null;

    public void Sync(Gate gate)
    {
        Reason = gate.Reason;
        Note = gate.Note ?? gate.Reason;
    }
}
