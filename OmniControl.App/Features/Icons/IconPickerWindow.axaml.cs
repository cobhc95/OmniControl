using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Icons;

/// Layout and wiring only: the icon picker's window. The search box has the keys on opening; the arrows move the selection wherever the focus is (Left/Right stay the text's while it holds words); Enter uses the selected icon.
public partial class IconPickerWindow : Window
{
    private static readonly string[] Patterns = { "*.svg", "*.png" };

    private readonly IconPickerModel? _model;

    public IconPickerWindow() => InitializeComponent();

    public IconPickerWindow(IconPickerModel model) : this()
    {
        _model = model;
        DataContext = model;
        AddHandler(KeyDownEvent, OnKeys, RoutingStrategies.Tunnel);
        Closed += (_, _) => model.Close();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }

    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if (_model is null) return;
        var inText = e.Source is TextBox { Text.Length: > 0 };
        switch (e.Key)
        {
            case Key.Up: _model.Move(0, -1); break;
            case Key.Down: _model.Move(0, 1); break;
            case Key.Left when !inText: _model.Move(-1, 0); break;
            case Key.Right when !inText: _model.Move(1, 0); break;
            case Key.Enter when e.Source is TextBox box && box != SearchBox:
                SearchBox.Focus(); // the name box: Enter keeps the typed name (it is taken as the box loses the keys)
                break;
            case Key.Enter:
                Finish(_model.Use());
                break;
            default: return;
        }
        e.Handled = true;
        Dispatcher.UIThread.Post(ShowSelected, DispatcherPriority.Background);
    }

    /// Scrolls the selected cell into view after an arrow moved the selection.
    private void ShowSelected() =>
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is IconCell { IsSelected: true })?.BringIntoView();

    private void OnCellClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IconCell cell) _model?.Select(cell);
    }

    private void OnCellDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IconCell cell && _model is { } model)
        {
            model.Select(cell);
            Finish(model.Use());
        }
    }

    private void OnUseClick(object? sender, RoutedEventArgs e) => Finish(_model?.Use() == true);

    private void OnDefaultClick(object? sender, RoutedEventArgs e)
    {
        _model?.UseDefault();
        Finish(true);
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e) => _model?.RemoveSelected();

    private async void OnAddClick(object? sender, RoutedEventArgs e) => await AddAsync(useAtOnce: false);

    private async void OnOwnImageClick(object? sender, RoutedEventArgs e) => await AddAsync(useAtOnce: true);

    /// Asks for an SVG or PNG and adds it to Your icons, selected; "Own image…" then uses it at once.
    private async Task AddAsync(bool useAtOnce)
    {
        if (_model is null || await PathPicker.PickAsync(this, PathKind.File, "Choose an icon", typeName: "Icons", patterns: Patterns) is not { } path) return;
        var problem = _model.Add(path);
        Problem.Text = problem ?? string.Empty;
        Problem.IsVisible = problem is not null;
        if (problem is null && useAtOnce) Finish(_model.Use());
    }

    private void Finish(bool answered)
    {
        if (answered) Close(_model?.Answer);
    }
}
