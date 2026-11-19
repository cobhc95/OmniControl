namespace OmniControl.App.Features.Volume.Fx;

/// How the FX windows are shown, the same for every window (app settings): above other windows, the effect list's width, the equaliser
/// graph's grid and phase curve, and each kind of effect's editor view.
public sealed record FxViewSettings(ISettingSlot<bool> KeepOnTop, ISettingSlot<double> ChainListWidth, ISettingSlot<bool> EqShowGrid, ISettingSlot<bool> EqShowPhase,
    ISettingSlot<Dictionary<string, EffectEditorPref>> EffectEditors);