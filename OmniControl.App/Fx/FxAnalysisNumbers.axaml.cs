using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Fx;

/// <summary>Layout only: the live numbers (Peak, Latency) of an <see cref="FxAnalysisViewModel"/>; the larger graph also says where the peak is.</summary>
public partial class FxAnalysisNumbers : UserControl
{
    /// <summary>The Peak also names its frequency ("+24.0 dB at 1.00 kHz").</summary>
    public static readonly StyledProperty<bool> ShowsPeakFrequencyProperty =
        AvaloniaProperty.Register<FxAnalysisNumbers, bool>(nameof(ShowsPeakFrequency));

    public FxAnalysisNumbers() => InitializeComponent();

    public bool ShowsPeakFrequency
    {
        get => GetValue(ShowsPeakFrequencyProperty);
        set => SetValue(ShowsPeakFrequencyProperty, value);
    }
}
