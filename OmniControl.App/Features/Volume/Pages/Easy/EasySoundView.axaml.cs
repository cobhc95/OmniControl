namespace OmniControl.App.Features.Volume.Pages.Easy;

/// The chips, curve and five sliders of one row's sound.
public partial class EasySoundView : UserControl
{
    public static readonly StyledProperty<bool> ShowsChipsProperty = AvaloniaProperty.Register<EasySoundView, bool>(nameof(ShowsChips), true);

    public EasySoundView() => InitializeComponent();

    /// False where the sound presets are chosen elsewhere (a row's Sound chip): the drawer then holds only the curve and sliders.
    public bool ShowsChips { get => GetValue(ShowsChipsProperty); set => SetValue(ShowsChipsProperty, value); }
}
