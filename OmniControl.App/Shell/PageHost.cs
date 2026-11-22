using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace OmniControl.App.Shell;

/// The main window's page area. A page's view is built when the page is laid out (so a window started in the tray builds nothing) and
/// kept hidden while another page shows, so going back is a visibility flip; <see cref="Kept"/> says which pages' views may stay (null:
/// all), and any other view is let go (<see cref="ViewLetGo"/>) and built again at its next opening. What makes a page look the same when
/// it comes back (its tab, open groups, scroll <see cref="PageViewModel.Scroll"/>) is kept in its view-model. A page stops its work when it
/// leaves the screen (<see cref="PageViewModel.OnOnScreenChanged"/>).
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));
    public static readonly StyledProperty<IReadOnlyList<PageViewModel>?> KeptProperty = AvaloniaProperty.Register<PageHost, IReadOnlyList<PageViewModel>?>(nameof(Kept));

    private readonly Dictionary<object, Control> _views = new();
    private object? _shown; // the page whose view is visible
    private ScrollViewer? _scroll; // the window's page scroller
    private bool _settling; // a page just came on: the scroller's moves are not the page's until its scroll is put back

    static PageHost()
    {
        PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.Follow());
        KeptProperty.Changed.AddClassHandler<PageHost>((host, _) => host.Follow());
    }

    public object? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }

    /// The pages whose views may stay built besides the open page's; null keeps every built view.
    public IReadOnlyList<PageViewModel>? Kept { get => GetValue(KeptProperty); set => SetValue(KeptProperty, value); }

    private void Follow()
    {
        var kept = Kept;
        foreach (var (page, view) in _views.ToList())
        {
            var open = ReferenceEquals(page, Page);
            if (!open && kept is not null && !kept.Contains(page))
            {
                ViewLetGo.Clear(view);
                Children.Remove(view);
                _views.Remove(page);
            }
            else view.IsVisible = open;
        }
        if (!ReferenceEquals(_shown, Page))
        {
            _shown = Page;
            if (Page is not null && _views.ContainsKey(Page)) Settle();
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Page is { } page && !_views.ContainsKey(page) && (this.FindDataTemplate(page) ?? FuncDataTemplate.Default).Build(page) is { } view)
        {
            view.DataContext = page;
            _views[page] = view;
            Children.Add(view);
            Settle();
        }
        return base.MeasureOverride(availableSize);
    }

    private void Settle()
    {
        _settling = true;
        Dispatcher.UIThread.Post(PutScrollBack, DispatcherPriority.Loaded);
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
        if (!_settling && Page is PageViewModel page && _scroll is { } scroll) page.Scroll = scroll.Offset;
    }

    private void PutScrollBack()
    {
        _settling = false;
        if (Page is PageViewModel page && _scroll is { } scroll) scroll.Offset = page.Scroll;
    }
}
