using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Whether a Volume control works in the current sound mode (from <see cref="SoundModes.Check"/>): when it does not, the control stays
/// visible but dimmed with the reason as tooltip, and a small "Switch" link changes to the mode that allows it. A Switch link is offered
/// only when that mode can be chosen on this PC now (<see cref="SoundModes.ModeUnavailable"/>); otherwise the reason says so instead.
/// </summary>
public sealed partial class ModeGate : ObservableObject
{
    private readonly Action<SoundMode> _switch;
    private readonly Func<SoundMode, string?>? _modeWhy;
    private SoundMode? _switchTo;

    /// <param name="switchMode">Changes the sound mode.</param>
    /// <param name="modeWhy">Why a mode can't be chosen now (null = it can); null function = every mode can.</param>
    public ModeGate(Action<SoundMode> switchMode, Func<SoundMode, string?>? modeWhy = null)
    {
        _switch = switchMode;
        _modeWhy = modeWhy;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAvailable))] private string? _reason;

    [ObservableProperty] private bool _canSwitch;

    /// <summary>The reason as one short sentence for a small pop-down (the gate's note, else its reason); null = the control works.</summary>
    [ObservableProperty] private string? _note;

    /// <summary>The Switch link's words when it names its mode ("Switch to Full control").</summary>
    [ObservableProperty] private string _switchLabel = "Switch";

    public bool IsAvailable => Reason is null;

    public void Sync(Gate gate)
    {
        var modeWhy = gate.Reason is not null && gate.SwitchTo is { } to ? _modeWhy?.Invoke(to) : null;
        Reason = Joined(gate.Reason, modeWhy);
        Note = Joined(gate.Note ?? gate.Reason, modeWhy);
        SwitchLabel = gate.SwitchTo is { } mode ? $"Switch to {mode.Name}" : "Switch";
        _switchTo = gate.SwitchTo;
        CanSwitch = gate.Reason is not null && gate.SwitchTo is not null && modeWhy is null;
    }

    /// <summary>The gate's words, then why its mode can't be chosen (when it can't), as one sentence each.</summary>
    private static string? Joined(string? text, string? modeWhy) =>
        text is null || modeWhy is null ? text : $"{text.TrimEnd('.')}. {modeWhy}";

    [RelayCommand]
    private void Switch()
    {
        if (_switchTo is { } mode) _switch(mode);
    }
}
