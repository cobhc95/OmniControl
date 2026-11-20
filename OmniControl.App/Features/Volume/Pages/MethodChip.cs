using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// A Mixer row's Method chip: the method its sound runs through now (<see cref="EffectsRules.ChipMethodOf"/>), in its words
/// (<see cref="EffectsRules.ChipWord"/>) and its colour class: Equalizer APO green only while it really runs (<paramref name="Stand"/>), red
/// when it is on the device but does not run, plain when it is not on it yet; a cable (its own, or the master's cable bus) blue, recording
/// the accent, direct muted (Styles/RowControls.axaml).
public sealed record MethodChip(RunMethod Method, ApoStand Stand = ApoStand.Running)
{
    public static MethodChip Of(RunMethod method, ApoStand stand = ApoStand.Running) => new(method, stand);

    private bool InApo => Method is RunMethod.Apo or RunMethod.ApoCable;

    public string Label => IsRisk ? $"{EffectsRules.ChipWord(Method)} · not running" : EffectsRules.ChipWord(Method);
    public bool IsApo => InApo && Stand == ApoStand.Running;
    public bool IsRisk => InApo && Stand == ApoStand.NotRunning;
    public bool IsCable => Method is RunMethod.Cable or RunMethod.Bus;
    public bool IsRecording => Method == RunMethod.Recording;
}
