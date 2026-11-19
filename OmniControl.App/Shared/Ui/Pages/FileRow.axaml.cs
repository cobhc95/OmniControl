namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: the file or folder picker of a <see cref="FileItem"/>, asking for the kind of file the item names.
public partial class FileRow : UserControl
{
    public FileRow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not FileItem { Kind: var kind }) return;
        foreach (var field in new[] { PathBoxFull, PathBoxHalf })
        {
            field.TypeName = kind.TypeName;
            field.Patterns = kind.Patterns;
        }
    }

    // A typed or browsed path is applied only when it exists; otherwise the box goes back to the path in use.
    private void OnChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is not PathField field || DataContext is not FileItem item) return;
        if (field.Kind == PathKind.Folder ? Directory.Exists(field.Text) : File.Exists(field.Text)) item.Choose(field.Text);
        else field.Text = item.Path;
    }
}
