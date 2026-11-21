namespace OmniControl.App.Shared.Ui.Controls;

/// THE colour editor's model (<see cref="ColourEditor"/>, made fresh at each opening): the colour as hue, saturation and brightness (HSV; the
/// square, the hue bar and the three sliders all edit these) and as "#RRGGBB" text. Every change is used at once (no Apply step).
public partial class ColourEditorModel : ObservableObject
{
    private readonly Action<Color> _useColour;
    private readonly HsvColor _reset;

    // True while one form of the colour is being written from the other, so the change is not sent back.
    private bool _syncing;

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

    [ObservableProperty] private IBrush _swatch;

    public string HueText => $"{Math.Round(Hue)}°";
    public string SaturationText => $"{Math.Round(Saturation)} %";
    public string BrightnessText => $"{Math.Round(Brightness)} %";

    /// Where a double-click puts the colour sliders: the default colour's own values.
    public double HueReset => Math.Round(_reset.H);
    public double SaturationReset => Math.Round(_reset.S * 100);
    public double BrightnessReset => Math.Round(_reset.V * 100);

    /// <param name="colour">The colour to start from.</param>
    /// <param name="reset">The default colour (the sliders' double-click).</param>
    /// <param name="useColour">Uses and keeps a new colour.</param>
    public ColourEditorModel(Color colour, Color reset, Action<Color> useColour)
    {
        (_useColour, _reset) = (useColour, reset.ToHsv());
        var hsv = colour.ToHsv();
        (_hue, _saturation, _brightness) = (hsv.H, hsv.S * 100, hsv.V * 100);
        _hex = ColorMath.Hex(colour);
        _swatch = new SolidColorBrush(colour);
    }

    partial void OnHueChanged(double value) => FromHsv();
    partial void OnSaturationChanged(double value) => FromHsv();
    partial void OnBrightnessChanged(double value) => FromHsv();

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
