using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

public partial class RuleLineRow : UserControl
{
    private IDisposable? _containerShown;

    public RuleLineRow() => InitializeComponent();

    // As a card list's own item its row container would still draw an empty, focusable card while the line hides, so it hides with the line.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.FindAncestorOfType<ListBoxItem>() is { } container && ReferenceEquals(container.DataContext, DataContext))
            _containerShown = container.Bind(IsVisibleProperty, Line.GetObservable(IsVisibleProperty));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _containerShown?.Dispose();
        _containerShown = null;
        base.OnDetachedFromVisualTree(e);
    }
}
