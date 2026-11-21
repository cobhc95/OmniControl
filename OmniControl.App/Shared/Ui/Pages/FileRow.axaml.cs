namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: a path typed or browsed in the card's path field (whichever layout shows it) is applied to the <see cref="FileItem"/>.
public partial class FileRow : UserControl
{
    public FileRow()
    {
        InitializeComponent();
        AddHandler(PathField.ChosenEvent, OnChosen);
    }

    // A typed or browsed path is applied only when it exists; otherwise the box goes back to the path in use.
    private void OnChosen(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not PathField field || DataContext is not FileItem item) return;
        if (field.Kind == PathKind.Folder ? Directory.Exists(field.Text) : File.Exists(field.Text)) item.Choose(field.Text);
        else field.Text = item.Path;
    }
}
