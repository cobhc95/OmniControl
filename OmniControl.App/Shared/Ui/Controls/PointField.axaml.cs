using Avalonia.Data;

namespace OmniControl.App.Shared.Ui.Controls;

/// The master field of a setting that is a point on the screen: shows it and the "Pick…" pill that opens the <see cref="PointPicker"/>.
public partial class PointField : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<PointField, string>(nameof(Value), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public PointField() => InitializeComponent();

    /// The point as <see cref="ScreenPointText"/> keeps it; empty until one is picked.
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty) return;
        var shown = ScreenPointText.Show(Value);
        ShownText.Text = shown.Length > 0 ? shown : "Not picked yet";
        ShownText.Classes.Set("muted", shown.Length == 0);
    }

    /// Opens the picker and keeps the point it returns (nothing changes when it is cancelled).
    public async Task PickAsync()
    {
        if (await PointPicker.PickAsync() is { } point) Value = ScreenPointText.Write(point.X, point.Y);
    }

    private async void OnPickClick(object? sender, RoutedEventArgs e) => await PickAsync();
}
