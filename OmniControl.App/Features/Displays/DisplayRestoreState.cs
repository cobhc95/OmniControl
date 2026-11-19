namespace OmniControl.App.Features.Displays;

/// What "Restore defaults" on the Displays page covers (pure): every display's software picture, the colour effect (saturation and invert,
/// both inside <see cref="DisplayPreset"/>) and whether warm light is on. The warm-light times and temperature, the saved presets and
/// every monitor's own (DDC/CI) settings are not part of it.
public sealed class DisplayRestoreState
{
    public DisplayPreset Picture { get; set; } = new();
    public bool WarmLight { get; set; }

    /// The same displays with every picture value neutral, saturation neutral, invert off and warm light off.
    public static DisplayRestoreState DefaultsOf(DisplayRestoreState current)
    {
        var picture = new DisplayPreset { Saturation = ColorMatrix.SaturationNeutral };
        foreach (var id in current.Picture.Displays.Keys) picture.Displays[id] = new PictureValues();
        return new DisplayRestoreState { Picture = picture };
    }

    public const string Scope =
        "Puts every display's software picture back to normal (brightness, contrast, gamma, colour channels, levels, saturation, temperature, grayscale, invert) "
        + "and turns warm light off. Your display presets are kept. Monitor settings are not changed.";
}
