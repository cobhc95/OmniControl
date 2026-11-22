namespace OmniControl.App.Features.Displays.Pages;

/// How a card shows a model's control state: greyed out with the reason, or usable.
internal static class CardMirror
{
    public static void Show(Availability availability, string? reason)
    {
        if (reason is null) availability.MakeAvailable();
        else availability.MakeUnavailable(reason);
    }

    /// The slider takes the model's value without writing it back, and its availability.
    public static void Show(SliderItem slider, int value, string? reason)
    {
        if (slider.Value != value) slider.Load(value);
        Show(slider.Availability, reason);
    }
}
