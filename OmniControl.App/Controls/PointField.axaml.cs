using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>The master field of a setting that is a point on the screen: shows it and the "Pick…" pill that opens the <see cref="PointPicker"/>.</summary>
public partial class PointField : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<PointField, string>(nameof(Value), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public PointField() => InitializeComponent();

    /// <summary>The point as <see cref="ScreenPointText"/> keeps it; empty until one is picked.</summary>
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty) return;
        var shown = ScreenPointText.Show(Value);
        ShownText.Text = shown.Length > 0 ? shown : "Not picked yet";
        ShownText.Classes.Set("muted", shown.Length == 0);
    }

    /// <summary>Opens the picker and keeps the point it returns (nothing changes when it is cancelled).</summary>
    public async Task PickAsync()
    {
        if (await PointPicker.PickAsync() is { } point) Value = ScreenPointText.Write(point.X, point.Y);
    }

    private async void OnPickClick(object? sender, RoutedEventArgs e) => await PickAsync();
}
