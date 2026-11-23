using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Displays.Pages;

public sealed partial class DisplaysService
{
    private const string GrayscaleId = "displays.sw.grayscale", InvertId = "displays.sw.invert";
    private const string PictureWords = "The software picture settings in OmniControl.";

    private static string DigitalId(SoftwareDisplayModel display, SoftwareLevel level) => $"displays.{display.Id}.{DisplayControls.SoftwareKey(level.Spec)}";

    private void RegisterDigital(SoftwareDisplayModel display)
    {
        _states.Register(DisplayControls.Software.Select(level => new StateEntry(DigitalId(display, level), $"{level.Spec.Label} ({display.Name})",
            Title, StateKind.Level, () => display.Reason is null ? StateValue.Of(display[level]) : null, PictureWords,
            Heading: DisplayControls.SoftwareGroup, Min: level.Spec.Min, Max: level.Spec.Max, Keywords: level.Spec.Keywords,
            Set: value => { if (value.Number is { } number) display.Set(level, (int)Math.Round(number)); })));
    }

    private void ForgetDigital(SoftwareDisplayModel display) => _states.Unregister(DisplayControls.Software.Select(level => DigitalId(display, level)));

    private void RegisterDesktopStates()
    {
        var spec = DisplayControls.SoftwareSaturation;
        _states.Register(new[]
        {
            new StateEntry(SaturationId, spec.Label, Title, StateKind.Level, () => StateValue.Of(_desktop.Saturation), PictureWords,
                Heading: DisplayControls.SoftwareGroup, Min: spec.Min, Max: spec.Max, Keywords: spec.Keywords,
                Set: value => { if (value.Number is { } number) _desktop.SetSaturation((int)Math.Round(number)); }),
            new StateEntry(GrayscaleId, DesktopPictureModel.GrayscaleLabel, Title, StateKind.OnOff, () => StateValue.Of(_desktop.Grayscale), PictureWords,
                Heading: DisplayControls.SoftwareGroup, Set: value => _desktop.SetGrayscale(value.Number != 0)),
            new StateEntry(InvertId, DesktopPictureModel.InvertLabel, Title, StateKind.OnOff, () => StateValue.Of(_desktop.Invert), PictureWords,
                Heading: DisplayControls.SoftwareGroup, Set: value => _desktop.SetInvert(value.Number != 0)),
        });
        _desktop.Changed += NotifyDesktop;
    }

    private static string SaturationId => $"displays.{DisplayControls.SoftwareKey(DisplayControls.SoftwareSaturation)}";
    private void NotifyDesktop() => _states.Notify(new[] { SaturationId, GrayscaleId, InvertId });

    private void NotifyDigital() => _states.Notify(_softwareDisplays.SelectMany(d => DisplayControls.Software.Select(level => DigitalId(d, level))).ToList());
}
