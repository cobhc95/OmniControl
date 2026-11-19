using Avalonia.Controls.Primitives;
using Avalonia.Input;
using OmniControl.App.Features.AppLists;

namespace OmniControl.App.Shared.Ui.Controls;

/// The cogwheel of a card that can be limited to apps: it opens the "Works in" menu on the card's <see cref="Scope"/> (an "Action
/// settings..." row first when <see cref="ShowsSettings"/>); without a scope it raises <see cref="SettingsClicked"/> at once, as a plain
/// cogwheel does. Layout and wiring only.
public partial class ScopeCog : UserControl
{
    public static readonly StyledProperty<AppScopeModel?> ScopeProperty = AvaloniaProperty.Register<ScopeCog, AppScopeModel?>(nameof(Scope));
    public static readonly StyledProperty<bool> ShowsSettingsProperty = AvaloniaProperty.Register<ScopeCog, bool>(nameof(ShowsSettings));
    public static readonly StyledProperty<string> CogTipProperty = AvaloniaProperty.Register<ScopeCog, string>(nameof(CogTip), string.Empty);

    public ScopeCog() => InitializeComponent();

    /// The card's scope; null for a card that has none.
    public AppScopeModel? Scope { get => GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }

    /// The card's action has settings: the menu offers them first.
    public bool ShowsSettings { get => GetValue(ShowsSettingsProperty); set => SetValue(ShowsSettingsProperty, value); }

    /// The cogwheel's tooltip, which the card words (what it opens depends on the card).
    public string CogTip { get => GetValue(CogTipProperty); set => SetValue(CogTipProperty, value); }

    /// The cogwheel itself, for the card to give the keyboard back to.
    public Button Opener => Cog;

    /// The action's settings were asked for (the menu's first row, or the cogwheel of a card without a scope).
    public event EventHandler<RoutedEventArgs>? SettingsClicked;

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (Scope is null) SettingsClicked?.Invoke(this, e);
        else FlyoutBase.ShowAttachedFlyout(this);
    }

    private void OnSettingsRowClick(object? sender, RoutedEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(this)?.Hide();
        SettingsClicked?.Invoke(this, e);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: StackPanel panel } flyout || Scope is not { } scope) return;
        panel.DataContext = this;
        panel.Children.OfType<AppScopeMenuView>().First().Open(flyout, scope);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: StackPanel panel }) panel.Children.OfType<AppScopeMenuView>().First().Release();
        Cog.Focus(NavigationMethod.Directional);
    }
}
