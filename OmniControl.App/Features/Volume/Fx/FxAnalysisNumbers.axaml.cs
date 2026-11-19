namespace OmniControl.App.Features.Volume.Fx;

/// Layout only: the live numbers (Peak, Latency) of an <see cref="FxAnalysisViewModel"/>; the larger graph also says where the peak is.
public partial class FxAnalysisNumbers : UserControl
{
    /// The Peak also names its frequency ("+24.0 dB at 1.00 kHz").
    public static readonly StyledProperty<bool> ShowsPeakFrequencyProperty =
        AvaloniaProperty.Register<FxAnalysisNumbers, bool>(nameof(ShowsPeakFrequency));

    public FxAnalysisNumbers() => InitializeComponent();

    public bool ShowsPeakFrequency
    {
        get => GetValue(ShowsPeakFrequencyProperty);
        set => SetValue(ShowsPeakFrequencyProperty, value);
    }
}
