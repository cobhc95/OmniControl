using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using OmniControl.App.Pages;

namespace OmniControl.App.Controls;

/// <summary>
/// The slider-with-typed-value piece (see SliderField.axaml): <see cref="Value"/> between <see cref="Minimum"/> and <see cref="Maximum"/>,
/// double-click on the slider goes back to <see cref="ResetValue"/>, and <see cref="Text"/> is what the value is called beside the slider.
/// Layout only: the slider's own behaviours (wheel, keys, reset) and the value's typing are the app-wide registered ones.
/// </summary>
public partial class SliderField : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SliderField, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SliderField, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ResetValueProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ResetValue));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SliderField, string?>(nameof(Text));
    public static readonly StyledProperty<bool> ThickProperty = AvaloniaProperty.Register<SliderField, bool>(nameof(Thick));
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<SliderField, bool>(nameof(Compact));
    public static readonly StyledProperty<double> ValueGapProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ValueGap));
    public static readonly StyledProperty<double> ValueMinWidthProperty = AvaloniaProperty.Register<SliderField, double>(nameof(ValueMinWidth));

    public SliderField()
    {
        InitializeComponent();
        // The slider and the typed number tell Value only when the user changes them; setting them from <see cref="Value"/>
        // never writes back, so a limit that arrives after the value cannot clamp the stored value.
        Bar.ValueChanged += (_, e) => { if (!_applying) Value = e.NewValue; };
        Typed.PropertyChanged += (_, e) => { if (!_applying && e.Property == NumberText.ValueProperty) Value = Typed.Value; };
    }

    private bool _applying;

    private void ShowValue()
    {
        _applying = true;
        (Bar.Minimum, Bar.Maximum, Typed.Minimum, Typed.Maximum) = (Minimum, Maximum, Minimum, Maximum);
        (Bar.Value, Typed.Value) = (Value, Value);
        _applying = false;
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>What a double-click on the slider sets (the value's default; stated where the slider is used).</summary>
    public double ResetValue { get => GetValue(ResetValueProperty); set => SetValue(ResetValueProperty, value); }

    /// <summary>The value as words ("50 %", "+3 dB").</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>The thick slider (<c>Slider.thick</c>) instead of the card list's default.</summary>
    public bool Thick { get => GetValue(ThickProperty); set => SetValue(ThickProperty, value); }

    /// <summary>The small slider (<c>Slider.compact</c>).</summary>
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    /// <summary>The least width of the value words (default: SliderValueMinWidth), so a changing number does not move the slider.</summary>
    public double ValueMinWidth { get => GetValue(ValueMinWidthProperty); set => SetValue(ValueMinWidthProperty, value); }

    /// <summary>The space between the slider and its value when a use sets its own (default: SliderValueGap).</summary>
    public double ValueGap { get => GetValue(ValueGapProperty); set => SetValue(ValueGapProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThickProperty) Bar.Classes.Set("thick", Thick);
        else if (change.Property == CompactProperty) Bar.Classes.Set("compact", Compact);
        else if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty) ShowValue();
        else if (change.Property == ResetValueProperty) SliderReset.SetValue(Bar, ResetValue);
        else if (change.Property == TextProperty) Words.Text = Text;
        else if (change.Property == ValueMinWidthProperty) Words.MinWidth = ValueMinWidth;
        else if (change.Property == ValueGapProperty) Layout.ColumnSpacing = ValueGap;
    }
}
