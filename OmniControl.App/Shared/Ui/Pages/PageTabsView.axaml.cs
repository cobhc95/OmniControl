using System.ComponentModel;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only for <see cref="PageTabs"/>: Left/Right on a tab open the neighbouring one (wrapping), and when a switch hides the control
/// that had the keyboard, the open tab takes it. <see cref="Attach"/> gives a page Ctrl+Tab / Ctrl+Shift+Tab.
public partial class PageTabsView : UserControl
{
    private PageTabs? _tabs;

    public PageTabsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Follow(DataContext as PageTabs);
        DetachedFromVisualTree += (_, _) => Follow(null);
        AttachedToVisualTree += (_, _) => Follow(DataContext as PageTabs);
    }

    /// Ctrl+Tab and Ctrl+Shift+Tab anywhere on <paramref name="page"/> open the next or previous tab of its visible tab strip.
    public static void Attach(Control page) => page.AddHandler(KeyDownEvent, (_, e) =>
    {
        if (e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.Control) return;
        if (page.GetVisualDescendants().OfType<PageTabsView>().FirstOrDefault(v => v.IsEffectivelyVisible) is not { _tabs: { } tabs } view) return;
        tabs.Step(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        view.FocusSelectedLater(NavigationMethod.Directional);
        e.Handled = true;
    }, RoutingStrategies.Tunnel);

    /// Puts the keyboard on the open tab once the switch has been laid out; <paramref name="how"/> = Directional shows the keyboard's
    /// focus look (a key switched), Unspecified does not (a click switched).
    private void FocusSelectedLater(NavigationMethod how) => Dispatcher.UIThread.Post(() => FocusSelected(how), DispatcherPriority.Loaded);

    private void FocusSelected(NavigationMethod how) =>
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is PageTab { IsSelected: true })?.Focus(how);

    private void Follow(PageTabs? tabs)
    {
        if (_tabs is not null) _tabs.PropertyChanged -= OnTabsChanged;
        _tabs = tabs;
        if (_tabs is not null && this.IsAttachedToVisualTree()) _tabs.PropertyChanged += OnTabsChanged;
    }

    private void OnTabsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Selected)) return;
        // A click on another tab, or a jump from the page, can hide the control that had the keyboard: the open tab takes it then.
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual { IsEffectivelyVisible: false }) FocusSelected(NavigationMethod.Unspecified);
        }, DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tabs is null || e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None || e.Source is not Button) return;
        _tabs.Step(e.Key == Key.Right ? 1 : -1);
        FocusSelectedLater(NavigationMethod.Directional);
        e.Handled = true;
    }
}
