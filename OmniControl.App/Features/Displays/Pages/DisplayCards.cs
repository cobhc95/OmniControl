namespace OmniControl.App.Features.Displays.Pages;

/// The search cards of the Displays page's controls, defined once: the cards are made from them (label, tooltip, choices) and the page's
/// search lists them (<see cref="DisplaysPageViewModel"/>), so what the card says is what search finds.
internal static class DisplayCards
{
    private const string Screen = "monitor display screen ";
    private const string Software = Screen + "digital software ";

    public static readonly SearchCard Saturation = new(DisplayControls.SoftwareSaturation.Label, DisplayControls.SoftwareSaturation.Keywords, DisplayControls.SoftwareGroup),
        Grayscale = new(DesktopPictureModel.GrayscaleLabel, Software + "greyscale gray black white colour saturation", DisplayControls.SoftwareGroup)
        {
            Tip = "Saturation 0 for every display; turning it off brings back the saturation you had",
        },
        Invert = new(DesktopPictureModel.InvertLabel, Software + "negative colour color", DisplayControls.SoftwareGroup)
        {
            Tip = "Windows' colour effect for every display; the same picture a negative would give",
        },
        Orientation = new(DisplayOrientationModel.Label, Screen + "ddc rotate rotation turn landscape portrait flip", DisplayControls.HardwareGroup)
        {
            Choices = DisplayOrientationModel.Names,
            Tip = "Turns the screen through Windows. Its width and height swap between landscape and portrait.",
        },
        WarmLight = new(WarmLightModel.Label, Software + "night light schedule evening warm kelvin", DisplayControls.SoftwareGroup) { Choices = WarmLightModel.StateButtons },
        WarmFrom = new(WarmLightModel.StartLabel, Software + "night light schedule evening start time", DisplayControls.SoftwareGroup) { Choices = WarmLightModel.TimeNames },
        WarmUntil = new(WarmLightModel.EndLabel, Software + "night light schedule morning end time", DisplayControls.SoftwareGroup) { Choices = WarmLightModel.TimeNames },
        WarmTemperature = new(WarmLightModel.TargetLabel, Software + "night light schedule kelvin temperature", DisplayControls.SoftwareGroup);

    /// The whole desktop's software cards, saturation first (the page shows them in this order).
    public static readonly IReadOnlyList<SearchCard> Desktop = new[]
    {
        Saturation, Grayscale, Invert, WarmLight, WarmFrom, WarmUntil, WarmTemperature,
    };

    /// A slider of a display (a monitor's level or a software control).
    public static SearchCard Level(LevelSpec spec, string group) => new(spec.Label, spec.Keywords + (group == DisplayControls.HardwareGroup ? " ddc" : string.Empty), group);

    /// A monitor's list of values (colour preset, input source), with the names the standard gives them.
    public static SearchCard Choice(HardwareChoice choice) =>
        new(choice.Feature.Label, Screen + "ddc " + choice.Feature.Keywords, DisplayControls.HardwareGroup) { Choices = VcpNames.NamesOf(choice.Feature.Code) };

    /// The card as one display's: its result says which display.
    public static SearchCard Of(SearchCard card, string display) => card with { Keywords = $"{card.Keywords} {display}", Title = $"{card.Label} ({display})" };
}
