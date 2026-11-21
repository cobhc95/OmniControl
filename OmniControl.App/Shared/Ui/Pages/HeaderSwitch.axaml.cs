using Avalonia.Data;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE compact switch of a setting on a group's header line (Packet filter, Use microphones, Show closed): <see cref="Label"/> and <see cref="IsOn"/>.
public partial class HeaderSwitch : UserControl
{
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<HeaderSwitch, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<bool> IsOnProperty =
        AvaloniaProperty.Register<HeaderSwitch, bool>(nameof(IsOn), defaultBindingMode: BindingMode.TwoWay);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

    public HeaderSwitch() => InitializeComponent();
}
