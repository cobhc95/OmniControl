using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Displays;

public partial class DisplaysPage : UserControl
{
    public DisplaysPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Listen(DataContext as DisplaysPageViewModel);
        DetachedFromVisualTree += (_, _) => Listen(null);
    }

    private DisplaysPageViewModel? _page;

    /// <summary>The page asks for the presets window through an event; opening a window is this view's job. Only a view on screen listens.</summary>
    private void Listen(DisplaysPageViewModel? page)
    {
        if (_page is not null) _page.PresetsRequested -= OnPresetsRequested;
        _page = page;
        if (_page is not null) _page.PresetsRequested += OnPresetsRequested;
    }

    private static void OnPresetsRequested(DisplayPresetsModel model) => _ = DisplayPresetsWindow.ShowAsync(model);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5) return;
        Refresh();
        e.Handled = true;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void Refresh() => (DataContext as DisplaysPageViewModel)?.ReloadCommand.Execute(null);
}
