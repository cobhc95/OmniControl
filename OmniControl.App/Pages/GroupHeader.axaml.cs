using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages;

/// <summary>The clickable header of a <see cref="CardGroup"/> panel: chevron, name, count.</summary>
public partial class GroupHeader : UserControl
{
    public static readonly StyledProperty<bool> ShowsSummaryProperty = AvaloniaProperty.Register<GroupHeader, bool>(nameof(ShowsSummary), true);

    public GroupHeader()
    {
        InitializeComponent();
        ShowsSummaryProperty.Changed.AddClassHandler<GroupHeader>((header, e) => header.SummaryHost.IsVisible = e.NewValue is true);
    }

    /// <summary>False: the header leaves its count to the host, which draws it after its own tools (a sub-group's power switch sits left of the count).</summary>
    public bool ShowsSummary { get => GetValue(ShowsSummaryProperty); set => SetValue(ShowsSummaryProperty, value); }

    private void OnClick(object? sender, RoutedEventArgs e) => (DataContext as CardGroup)?.Toggle();
}
