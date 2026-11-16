using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>Layout only: one set-up stage; <see cref="Stage"/> is its state, <see cref="Body"/> what shows while it is open.</summary>
public partial class SetupStage : UserControl
{
    public static readonly StyledProperty<SetupStageItem?> StageProperty = AvaloniaProperty.Register<SetupStage, SetupStageItem?>(nameof(Stage));
    public static readonly StyledProperty<object?> BodyProperty = AvaloniaProperty.Register<SetupStage, object?>(nameof(Body));

    public SetupStage() => InitializeComponent();

    public SetupStageItem? Stage { get => GetValue(StageProperty); set => SetValue(StageProperty, value); }
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
}
