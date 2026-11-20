namespace OmniControl.App.Features.Settings.Pages;

/// The accent colour editor behind the Accent colour gallery's "Custom colour…" row (AccentEditor.axaml, made fresh at each opening): the custom
/// colour as hue, saturation and brightness (HSV; the square, the hue bar and the three sliders all edit these) and as "#RRGGBB" text, plus
/// the one Intensity setting of every accent choice. Every change is used at once through the page's callbacks (no Apply step).
public sealed partial class AccentEditorModel : ObservableObject
{
    private readonly Action<Color> _useColour;
    private readonly Action<int> _useIntensity;
    private readonly Action _close;

    // True while one form of the colour is being written from the other, so the change is not sent back.
    private bool _syncing;

    /// Where a double-click puts the colour sliders: the default custom colour's own values.
    private static readonly HsvColor Default = Color.Parse(AccentService.DefaultCustomHex).ToHsv();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HueText))]
    private double _hue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaturationText))]
    private double _saturation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessText))]
    private double _brightness;

    /// The colour as typed or shown, "#RRGGBB".
    [ObservableProperty] private string _hex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntensityText))]
    private double _intensity;

    [ObservableProperty] private IBrush _swatch;

    public string HueText => $"{Math.Round(Hue)}°";
    public string SaturationText => $"{Math.Round(Saturation)} %";
    public string BrightnessText => $"{Math.Round(Brightness)} %";
    public string IntensityText => AccentService.IntensityWords(IntensityValue);

    public double HueReset => Math.Round(Default.H);
    public double SaturationReset => Math.Round(Default.S * 100);
    public double BrightnessReset => Math.Round(Default.V * 100);
    public double IntensityReset => AccentService.DefaultIntensity;
    public double IntensityMax => AccentService.MaxIntensity;

    private int IntensityValue => (int)Math.Round(Intensity);

    /// <param name="colour">The custom colour to start from.</param>
    /// <param name="intensity">The intensity in use.</param>
    /// <param name="useColour">Uses and keeps a new custom colour.</param>
    /// <param name="useIntensity">Uses and keeps a new intensity.</param>
    /// <param name="close">Closes the pop-down (the preview's Close).</param>
    public AccentEditorModel(Color colour, int intensity, Action<Color> useColour, Action<int> useIntensity, Action close)
    {
        _useColour = useColour;
        _useIntensity = useIntensity;
        _close = close;
        var hsv = colour.ToHsv();
        (_hue, _saturation, _brightness) = (hsv.H, hsv.S * 100, hsv.V * 100);
        _hex = ColorMath.Hex(colour);
        _swatch = new SolidColorBrush(colour);
        _intensity = intensity;
    }

    [RelayCommand]
    private void Close() => _close();

    partial void OnHueChanged(double value) => FromHsv();
    partial void OnSaturationChanged(double value) => FromHsv();
    partial void OnBrightnessChanged(double value) => FromHsv();

    partial void OnIntensityChanged(double value) => _useIntensity(IntensityValue);

    /// Typed text: used as soon as it is a whole colour; the text itself is left as typed.
    partial void OnHexChanged(string value)
    {
        if (_syncing || ColorMath.ParseHex(value) is not { } colour) return;
        var hsv = colour.ToHsv();
        _syncing = true;
        // A grey has no hue and black no saturation: keep the ones shown, so the square does not jump.
        if (hsv.S > 0 && hsv.V > 0) Hue = hsv.H;
        if (hsv.V > 0) Saturation = hsv.S * 100;
        Brightness = hsv.V * 100;
        _syncing = false;
        Use(colour);
    }

    private void FromHsv()
    {
        if (_syncing) return;
        var colour = new HsvColor(1, Math.Clamp(Hue, 0, 360), Math.Clamp(Saturation / 100, 0, 1), Math.Clamp(Brightness / 100, 0, 1)).ToRgb();
        _syncing = true;
        Hex = ColorMath.Hex(colour);
        _syncing = false;
        Use(colour);
    }

    private void Use(Color colour)
    {
        Swatch = new SolidColorBrush(colour);
        _useColour(colour);
    }
}
