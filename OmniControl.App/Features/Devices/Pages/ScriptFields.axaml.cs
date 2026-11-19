using Avalonia.Controls.Primitives;

namespace OmniControl.App.Features.Devices.Pages;

/// Wiring only: which rows show (<see cref="ShowAddress"/>, <see cref="ShowSignIn"/>, <see cref="ShowProfile"/>); the Router guide is given
/// the method when it opens (a pop-down's content does not inherit it); Export and Import ask for a file through <see cref="PathPicker"/>.
public partial class ScriptFields : UserControl
{
    public static readonly StyledProperty<bool> ShowAddressProperty = AvaloniaProperty.Register<ScriptFields, bool>(nameof(ShowAddress), true);
    public static readonly StyledProperty<bool> ShowSignInProperty = AvaloniaProperty.Register<ScriptFields, bool>(nameof(ShowSignIn), true);
    public static readonly StyledProperty<bool> ShowProfileProperty = AvaloniaProperty.Register<ScriptFields, bool>(nameof(ShowProfile), true);

    private static readonly string[] ProfilePatterns = { "*.json" };

    public ScriptFields()
    {
        InitializeComponent();
        Apply();
    }

    public bool ShowAddress { get => GetValue(ShowAddressProperty); set => SetValue(ShowAddressProperty, value); }
    public bool ShowSignIn { get => GetValue(ShowSignInProperty); set => SetValue(ShowSignInProperty, value); }
    public bool ShowProfile { get => GetValue(ShowProfileProperty); set => SetValue(ShowProfileProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowAddressProperty || change.Property == ShowSignInProperty || change.Property == ShowProfileProperty) Apply();
    }

    private void Apply()
    {
        AddressRow.IsVisible = ShowAddress;
        SignInRow.IsVisible = ShowSignIn;
        ProfileRow.IsVisible = ShowProfile;
    }

    private void OnGuideClick(object? sender, RoutedEventArgs e) => FlyoutBase.GetAttachedFlyout(this)?.ShowAt((Control)sender!);

    private void OnGuideOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control panel }) panel.DataContext = DataContext;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MethodItem item && await PathPicker.PickSaveAsync(this, "Export the template", item.ProfileFileName, ".json", "Templates", ProfilePatterns) is { } path)
            item.ExportProfile(path);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MethodItem item && await PathPicker.PickAsync(this, Shared.Services.PathKind.File, "Import a template", typeName: "Templates", patterns: ProfilePatterns) is { } path)
            item.ImportProfile(path);
    }
}
