using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A Mixer row's Method chip: the method its sound runs through now (<see cref="EffectsRules.ChipMethodOf"/>), in its words
/// (<see cref="EffectsRules.ChipWord"/>) and its colour class: Equalizer APO green, a cable (its own, or the master's cable bus) blue,
/// recording the accent, direct muted (Styles/RowControls.axaml).
/// </summary>
public sealed record MethodChip(RunMethod Method)
{
    public static MethodChip Of(RunMethod method) => new(method);

    public string Label => EffectsRules.ChipWord(Method);
    public bool IsApo => Method is RunMethod.Apo or RunMethod.ApoCable;
    public bool IsCable => Method is RunMethod.Cable or RunMethod.Bus;
    public bool IsRecording => Method == RunMethod.Recording;
}
