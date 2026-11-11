using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

public partial class FxWindow : Window
{
    public FxWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as FxChainViewModel)?.Dispose();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Opened += (_, _) => TakeKeyboard();
        Activated += (_, _) =>
        {
            if (FocusManager?.GetFocusedElement() is not Visual v || !this.IsVisualAncestorOf(v)) TakeKeyboard();
        };
    }

    /// <summary>Makes this window the active one and puts the keys in the chain list, so shortcuts work without a click first.</summary>
    private void TakeKeyboard()
    {
        Activate();
        Dispatcher.UIThread.Post(() => this.FindControl<FxChainView>("FxView")?.FocusChain(), DispatcherPriority.Input);
    }

    /// <summary>Esc closes the window, unless it is needed to finish something (an adjusted slider, an open list or menu).</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        // Shortcuts are routed from the window, so they work even when no control in it has the keyboard yet.
        if (e.Key != Key.Escape) { this.FindControl<FxChainView>("FxView")?.HandleKey(e); return; }
        if (RowEditing.IsEditing(FocusManager?.GetFocusedElement())) return;
        if (this.FindControl<Control>("FxView")?.GetVisualDescendants().OfType<Button>().Any(b => b.Flyout?.IsOpen == true) == true) return;
        Close();
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is FxChainViewModel vm) vm.CloseRequested += Close;
    }
}
