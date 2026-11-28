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
        WarmLight = new(WarmLightModel.Label, Software + "warm light night schedule evening blue light filter kelvin", DisplayControls.SoftwareGroup),
        WarmSchedule = new(WarmLightModel.ScheduleLabel, Software + "warm light night schedule timer evening morning auto", DisplayControls.SoftwareGroup),
        WarmFrom = new(WarmLightModel.StartLabel, Software + "warm light night schedule evening start time turn on", DisplayControls.SoftwareGroup) { Choices = WarmLightModel.TimeNames },
        WarmUntil = new(WarmLightModel.EndLabel, Software + "warm light night schedule morning end time turn off", DisplayControls.SoftwareGroup) { Choices = WarmLightModel.TimeNames },
        WarmTemperature = new(WarmLightModel.TargetLabel, Software + "warm light night schedule kelvin temperature", DisplayControls.SoftwareGroup);

    /// The whole desktop's software cards, saturation first (the page shows them in this order).
    public static readonly IReadOnlyList<SearchCard> Desktop = new[]
    {
        Saturation, Grayscale, Invert, WarmLight, WarmTemperature, WarmSchedule, WarmFrom, WarmUntil,
    };

    /// A slider of a display (a monitor's level or a software control).
    public static SearchCard Level(LevelSpec spec, string group) => new(spec.Label, spec.Keywords + (group == DisplayControls.HardwareGroup ? " ddc" : string.Empty), group);

    /// A monitor's list of values (colour preset, input source), with the names the standard gives them.
    public static SearchCard Choice(HardwareChoice choice) =>
        new(choice.Feature.Label, Screen + "ddc " + choice.Feature.Keywords, DisplayControls.HardwareGroup) { Choices = VcpNames.NamesOf(choice.Feature.Code) };

    /// The card as one display's: its result says which display.
    public static SearchCard Of(SearchCard card, string display) => card with { Keywords = $"{card.Keywords} {display}", Title = $"{card.Label} ({display})" };
}
