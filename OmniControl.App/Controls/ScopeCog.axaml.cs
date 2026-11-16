using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.AppLists;

namespace OmniControl.App.Controls;

/// <summary>
/// The cogwheel of a card that can be limited to apps: it opens the "Works in" menu on the card's <see cref="Scope"/> (an "Action
/// settings..." row first when <see cref="ShowsSettings"/>); without a scope it raises <see cref="SettingsClicked"/> at once, as a plain
/// cogwheel does. Layout and wiring only.
/// </summary>
public partial class ScopeCog : UserControl
{
    public static readonly StyledProperty<AppScopeModel?> ScopeProperty = AvaloniaProperty.Register<ScopeCog, AppScopeModel?>(nameof(Scope));
    public static readonly StyledProperty<bool> ShowsSettingsProperty = AvaloniaProperty.Register<ScopeCog, bool>(nameof(ShowsSettings));
    public static readonly StyledProperty<string> CogTipProperty = AvaloniaProperty.Register<ScopeCog, string>(nameof(CogTip), string.Empty);

    public ScopeCog() => InitializeComponent();

    /// <summary>The card's scope; null for a card that has none.</summary>
    public AppScopeModel? Scope { get => GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }

    /// <summary>The card's action has settings: the menu offers them first.</summary>
    public bool ShowsSettings { get => GetValue(ShowsSettingsProperty); set => SetValue(ShowsSettingsProperty, value); }

    /// <summary>The cogwheel's tooltip, which the card words (what it opens depends on the card).</summary>
    public string CogTip { get => GetValue(CogTipProperty); set => SetValue(CogTipProperty, value); }

    /// <summary>The cogwheel itself, for the card to give the keyboard back to.</summary>
    public Button Opener => Cog;

    /// <summary>The action's settings were asked for (the menu's first row, or the cogwheel of a card without a scope).</summary>
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
