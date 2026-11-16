using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Controls;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// Wiring only: a pickable square opens the icon pop-down (given the device when it opens, as a pop-down's content does not inherit it), the
/// colour dots are the shared <see cref="Swatches"/> (a click picks one), and "Own image…" asks for a picture through <see cref="PathPicker"/>.
/// A square that is not pickable (the list's) takes no clicks or focus.
/// </summary>
public partial class DeviceIconView : UserControl
{
    public static readonly StyledProperty<bool> PickableProperty = AvaloniaProperty.Register<DeviceIconView, bool>(nameof(Pickable));

    private static readonly string[] ImagePatterns = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" };

    public DeviceIconView()
    {
        InitializeComponent();
        Apply();
    }

    public bool Pickable { get => GetValue(PickableProperty); set => SetValue(PickableProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PickableProperty) Apply();
    }

    private void Apply()
    {
        Square.IsHitTestVisible = Pickable;
        Square.Focusable = Pickable;
    }

    private void OnSquareClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    private void OnMenuOpened(object? sender, EventArgs e)
    {
        Menu.DataContext = DataContext;
        ShowDots();
    }

    private void ShowDots()
    {
        if (DataContext is not DeviceItem item) return;
        var dots = Swatches.Build(item.IconColours, item.IconColourIndex);
        dots.AddHandler(PointerPressedEvent, OnDotPressed, RoutingStrategies.Tunnel);
        Dots.Content = dots;
    }

    private void OnDotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DeviceItem item || (e.Source as Control)?.Tag is not int index) return;
        item.PickColour(index);
        ShowDots();
        e.Handled = true;
    }

    private void OnIconPicked(object? sender, RoutedEventArgs e) => FlyoutBase.GetAttachedFlyout(this)?.Hide();

    private async void OnOwnImage(object? sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(this)?.Hide();
        if (DataContext is DeviceItem item && await PathPicker.PickAsync(this, PathKind.File, "Choose a picture", typeName: "Pictures", patterns: ImagePatterns) is { } path)
            item.PickOwnImage(path);
    }
}
