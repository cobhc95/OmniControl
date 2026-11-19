namespace OmniControl.App.Shared.Ui.Pages;

/// The clickable header of a <see cref="CardGroup"/> panel: chevron, name, count.
public partial class GroupHeader : UserControl
{
    public static readonly StyledProperty<bool> ShowsSummaryProperty = AvaloniaProperty.Register<GroupHeader, bool>(nameof(ShowsSummary), true);

    public GroupHeader()
    {
        InitializeComponent();
        ShowsSummaryProperty.Changed.AddClassHandler<GroupHeader>((header, e) => header.SummaryHost.IsVisible = e.NewValue is true);
    }

    /// False: the header leaves its count to the host, which draws it after its own tools (a sub-group's power switch sits left of the count).
    public bool ShowsSummary { get => GetValue(ShowsSummaryProperty); set => SetValue(ShowsSummaryProperty, value); }

    private void OnClick(object? sender, RoutedEventArgs e) => (DataContext as CardGroup)?.Toggle();
}
