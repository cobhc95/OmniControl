using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Whether a Volume control works in the current sound mode (from <see cref="SoundModes.Check"/>): when it does not, the control stays
/// visible but dimmed with the reason as tooltip, and a small "Switch" link changes to the mode that allows it.
/// </summary>
public sealed partial class ModeGate : ObservableObject
{
    private readonly Action<SoundMode> _switch;
    private SoundMode? _switchTo;

    public ModeGate(Action<SoundMode> switchMode) => _switch = switchMode;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAvailable))] private string? _reason;

    [ObservableProperty] private bool _canSwitch;

    /// <summary>The reason as one short sentence for a small pop-down (the gate's note, else its reason); null = the control works.</summary>
    [ObservableProperty] private string? _note;

    /// <summary>The Switch link's words when it names its mode ("Switch to Full control").</summary>
    [ObservableProperty] private string _switchLabel = "Switch";

    public bool IsAvailable => Reason is null;

    public void Sync(Gate gate)
    {
        Reason = gate.Reason;
        Note = gate.Note ?? gate.Reason;
        SwitchLabel = gate.SwitchTo is { } to ? $"Switch to {to.Name}" : "Switch";
        _switchTo = gate.SwitchTo;
        CanSwitch = gate.Reason is not null && gate.SwitchTo is not null;
    }

    [RelayCommand]
    private void Switch()
    {
        if (_switchTo is { } mode) _switch(mode);
    }
}
