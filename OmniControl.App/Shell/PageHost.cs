using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace OmniControl.App.Shell;

/// The main window's page area: it holds the open page's view only. The view is built when the page is laid out (so a window started in
/// the tray builds nothing) and let go as soon as another page or the search results show, so the window carries one page's visuals;
/// what makes a page look the same when it comes back (its tab, open groups, scroll <see cref="PageViewModel.Scroll"/>) is kept in its
/// view-model. A page stops its work when it leaves the screen (<see cref="PageViewModel.OnOnScreenChanged"/>).
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private object? _built; // the page whose view the host holds
    private ScrollViewer? _scroll; // the window's page scroller
    private bool _settling; // a view was just built: the scroller's moves are not the page's until its scroll is put back

    static PageHost() => PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.Follow());

    public object? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }

    private void Follow()
    {
        if (ReferenceEquals(_built, Page)) return;
        _built = null;
        Children.Clear();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_built is null && Page is { } page && (this.FindDataTemplate(page) ?? FuncDataTemplate.Default).Build(page) is { } view)
        {
            view.DataContext = page;
            _built = page;
            Children.Add(view);
            _settling = true;
            Dispatcher.UIThread.Post(PutScrollBack, DispatcherPriority.Loaded);
        }
        return base.MeasureOverride(availableSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null) _scroll.ScrollChanged += OnScrolled;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scroll is not null) _scroll.ScrollChanged -= OnScrolled;
        _scroll = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (!_settling && _built is PageViewModel page && _scroll is { } scroll) page.Scroll = scroll.Offset;
    }

    private void PutScrollBack()
    {
        _settling = false;
        if (_built is PageViewModel page && _scroll is { } scroll) scroll.Offset = page.Scroll;
    }
}
