namespace OmniControl.App.Features.Settings.Pages;

/// The accent colour editor behind the Accent colour gallery's "Custom colour…" row (AccentEditor.axaml, made fresh at each opening): THE colour
/// editor's custom colour (<see cref="ColourEditorModel"/>) plus the one Intensity setting of every accent choice. Every change is used at once
/// through the page's callbacks (no Apply step).
public sealed partial class AccentEditorModel : ColourEditorModel
{
    private readonly Action<int> _useIntensity;
    private readonly Action _close;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntensityText))]
    private double _intensity;

    public string IntensityText => AccentService.IntensityWords(IntensityValue);

    public double IntensityReset => AccentService.DefaultIntensity;
    public double IntensityMax => AccentService.MaxIntensity;

    private int IntensityValue => (int)Math.Round(Intensity);

    /// <param name="colour">The custom colour to start from.</param>
    /// <param name="intensity">The intensity in use.</param>
    /// <param name="useColour">Uses and keeps a new custom colour.</param>
    /// <param name="useIntensity">Uses and keeps a new intensity.</param>
    /// <param name="close">Closes the pop-down (the preview's Close).</param>
    public AccentEditorModel(Color colour, int intensity, Action<Color> useColour, Action<int> useIntensity, Action close)
        : base(colour, Color.Parse(AccentService.DefaultCustomHex), useColour)
    {
        (_useIntensity, _close) = (useIntensity, close);
        _intensity = intensity;
    }

    [RelayCommand]
    private void Close() => _close();

    partial void OnIntensityChanged(double value) => _useIntensity(IntensityValue);
}
