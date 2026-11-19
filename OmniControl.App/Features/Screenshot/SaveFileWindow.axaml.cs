using Avalonia.Input;

namespace OmniControl.App.Features.Screenshot;

/// Wiring only: shows a <see cref="SaveFileModel"/> and closes with Save (true) or anything else (false: Discard, Esc, the X).
public partial class SaveFileWindow : Window
{
    private readonly SaveFileModel _model;
    private bool _saved;

    public SaveFileWindow() : this(null!) { } // the designer only

    public SaveFileWindow(SaveFileModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Activate(); // above the screen being captured, with the keyboard
        NameBox.Focus();
        NameBox.SelectAll();
    }

    /// Shows the window and waits until it closes; true when Save was chosen.
    public Task<bool> AskAsync()
    {
        var done = new TaskCompletionSource<bool>();
        Closed += (_, _) => done.TrySetResult(_saved);
        OwnedWindow.Show(this);
        return done.Task;
    }

    private void OnFieldKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnSaveClick(sender, e);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _model.Folder = FolderField.Box.Text ?? string.Empty; // a path typed and not yet left
        if (!_model.CanSave) return;
        _saved = true;
        Close();
    }

    private void OnDiscardClick(object? sender, RoutedEventArgs e) => Close();
}
