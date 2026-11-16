using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.AppLists;

namespace OmniControl.App.Controls;

/// <summary>
/// Wiring only: the content of the "Works in" pop-down. Whoever hosts it in a flyout calls <see cref="Open"/> as the flyout opens (a
/// pop-down's content does not inherit the data context) and <see cref="Release"/> as it closes; the model asks for the flyout to close.
/// </summary>
public partial class AppScopeMenuView : UserControl
{
    private AppScopeModel? _model;
    private Flyout? _flyout;

    public AppScopeMenuView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    /// <summary>The pop-down opened: shows <paramref name="model"/>'s first page and closes <paramref name="flyout"/> when the model says so.</summary>
    public void Open(Flyout flyout, AppScopeModel model)
    {
        Release();
        _flyout = flyout;
        _model = model;
        DataContext = model;
        model.CloseRequested += Close;
        model.OpenMenu();
    }

    /// <summary>The pop-down closed.</summary>
    public void Release()
    {
        if (_model is not null) _model.CloseRequested -= Close;
        _model = null;
        _flyout = null;
    }

    private void Close() => _flyout?.Hide();

    private void OnManageClick(object? sender, RoutedEventArgs e) => _model?.OpenManager();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
