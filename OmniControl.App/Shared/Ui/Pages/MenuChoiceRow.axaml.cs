namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: the full and half-width chips' menus, each fed by the card's item (<see cref="MenuChoiceLink"/>).
public partial class MenuChoiceRow : UserControl
{
    private readonly MenuChoiceLink[] _links;

    public MenuChoiceRow()
    {
        InitializeComponent();
        _links = new[] { new MenuChoiceLink(FullChip, this), new MenuChoiceLink(HalfChip, this) };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var link in _links) link.Follow(DataContext as MenuChoiceItem);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var link in _links) link.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var link in _links) link.Detach();
    }
}
