using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>The chips, curve and five sliders of one row's sound.</summary>
public partial class EasySoundView : UserControl
{
    public static readonly StyledProperty<bool> ShowsChipsProperty = AvaloniaProperty.Register<EasySoundView, bool>(nameof(ShowsChips), true);

    public EasySoundView() => InitializeComponent();

    /// <summary>False where the sound presets are chosen elsewhere (a row's Sound chip): the drawer then holds only the curve and sliders.</summary>
    public bool ShowsChips { get => GetValue(ShowsChipsProperty); set => SetValue(ShowsChipsProperty, value); }
}
