using Avalonia.Data;
using OmniControl.App.Shared.Ui.Pages;

namespace OmniControl.App.Shared.Ui.Controls;

/// The slider-with-typed-value piece (see SliderField.axaml): <see cref="Value"/> between <see cref="Minimum"/> and <see cref="Maximum"/>,
/// double-click on the slider goes back to <see cref="ResetValue"/>, and <see cref="Text"/> is what the value is called beside the slider.
/// Layout only: the slider's own behaviours (wheel, keys, reset) and the value's typing are the app-wide registered ones.
public partial class SliderField : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SliderField, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SliderField, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ResetValueProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ResetValue));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SliderField, string?>(nameof(Text));
    public static readonly StyledProperty<double> ValueGapProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ValueGap));
    public static readonly StyledProperty<int> DecimalsProperty = AvaloniaProperty.Register<SliderField, int>(nameof(Decimals));
    public static readonly StyledProperty<double> ValueMinWidthProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ValueMinWidth));

    public SliderField()
    {
        InitializeComponent();
        // Attach the reset even when the reset is 0 (the default): binding 0 would never raise a change, so the double-click handler would never attach.
        SliderReset.SetValue(Bar, ResetValue);
        // The slider and the typed number tell Value only when the user changes them; setting them from <see cref="Value"/>
        // never writes back, so a limit that arrives after the value cannot clamp the stored value.
        Bar.ValueChanged += (_, e) => { if (!_applying) Value = e.NewValue; };
        Typed.PropertyChanged += (_, e) => { if (!_applying && e.Property == NumberText.ValueProperty) Value = Math.Round(Typed.Value * Scale); };
    }

    private bool _applying;

    private void ShowValue()
    {
        _applying = true;
        (Bar.Minimum, Bar.Maximum, Typed.Minimum, Typed.Maximum) = (Minimum, Maximum, Minimum / Scale, Maximum / Scale);
        (Bar.Value, Typed.Value, Typed.Decimals) = (Value, Value / Scale, Decimals);
        _applying = false;
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// Decimals of the value as it is typed: with 1 the slider's numbers count in tenths, so a slider at 15 types and shows as 1.5 (the OSD's
    /// seconds). 0: the slider's own numbers.
    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    private double Scale => Math.Pow(10, Decimals);
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// What a double-click on the slider sets (the value's default; stated where the slider is used).
    public double ResetValue { get => GetValue(ResetValueProperty); set => SetValue(ResetValueProperty, value); }

    /// The value as words ("50 %", "+3 dB").
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// The least width of the value words (default: SliderValueMinWidth), so a changing number does not move the slider.
    public double ValueMinWidth { get => GetValue(ValueMinWidthProperty); set => SetValue(ValueMinWidthProperty, value); }

    /// The space between the slider and its value when a use sets its own (default: SliderValueGap).
    public double ValueGap { get => GetValue(ValueGapProperty); set => SetValue(ValueGapProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty || change.Property == DecimalsProperty) ShowValue();
        else if (change.Property == ResetValueProperty) SliderReset.SetValue(Bar, ResetValue);
        else if (change.Property == TextProperty) Words.Text = Text;
        else if (change.Property == ValueMinWidthProperty) Words.MinWidth = ValueMinWidth;
        else if (change.Property == ValueGapProperty) Layout.ColumnSpacing = ValueGap;
    }
}
