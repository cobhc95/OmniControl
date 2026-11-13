using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the FX window hosts the chain's <see cref="FxChainPanel"/> and passes it every key; Esc it does not use closes the window.</summary>
public partial class FxWindow : Window
{
    public FxWindow()
    {
        InitializeComponent();
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
        Dispatcher.UIThread.Post(() => FxView.FocusChain(), DispatcherPriority.Input);
    }

    /// <summary>
    /// Shortcuts are routed from the window, so they work even when no control in it has the keyboard yet. Esc first closes the
    /// larger response graph; Esc the panel does not use closes the window, unless it is needed to finish something (an adjusted
    /// slider, an open list or menu).
    /// </summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Analysis.ClosePanel()) { e.Handled = true; return; }
        if (TabToGraph(e)) { e.Handled = true; return; }
        FxView.HandleKey(e);
        if (e.Handled || e.Key != Key.Escape) return;
        if (RowEditing.IsEditing(FocusManager?.GetFocusedElement())) return;
        if (FxView.GetVisualDescendants().OfType<Button>().Any(b => b.Flyout?.IsOpen == true)) return;
        Close();
        e.Handled = true;
    }

    /// <summary>
    /// The header's response graph in the panel's Tab cycle: Shift+Tab from the chain list goes to it, Tab from it to the list and
    /// Shift+Tab from it to the editor (the panel's own Tab still switches between its two panes). True when the key was used.
    /// </summary>
    private bool TabToGraph(KeyEventArgs e)
    {
        if (e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != 0) return false;
        var back = e.KeyModifiers == KeyModifiers.Shift;
        if (Analysis.HasKeyboard)
        {
            if (back) FxView.FocusEditor();
            else FxView.FocusChain();
            return true;
        }
        if (!back || !FxView.ListHasKeyboard) return false;
        Analysis.FocusGraph();
        return true;
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => FxView.AddEffect();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // The button sits in the chain panel's button row, whose data is the chain; it belongs to the window.
        ForgetButton.DataContext = DataContext;
        if (DataContext is FxWindowViewModel vm) vm.CloseRequested += Close;
    }
}
