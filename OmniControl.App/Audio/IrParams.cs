namespace OmniControl.App.Audio;

/// <summary>
/// Where each setting of the IR Loader sits in its slot's <see cref="FxSlot.Values"/> (the order of its <see cref="FxCatalog"/> entry:
/// stored in mixer.json, so new settings are only ever appended). Names and ranges follow TabForge's IR Loader.
/// </summary>
public static class IrParams
{
    public const int Mix = 0, Gain = 1, AutoLevel = 2, TrimSilence = 3;

    public static bool IsOn(double value) => value >= 0.5;

    /// <summary>Pads an older chain: a slot saved before a setting existed takes its default.</summary>
    public static double ValueOf(FxSlot slot, int index) => slot.Values.Count > index ? slot.Values[index] : FxCatalog.Find(FxCatalog.IrKind).Params[index].Default;
}
