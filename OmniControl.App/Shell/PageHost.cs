using Avalonia.Controls.Templates;

namespace OmniControl.App.Shell;

/// The main window's page area. A page's view is built the first time the page is laid out (so a window started in the tray builds
/// nothing) and then kept, hidden while another page shows, so going back to a page is a visibility flip instead of a rebuild (the
/// Resources page keeps its gadget tab the same way). A null <see cref="Page"/> (<see cref="ShellViewModel.ShownPage"/>: the window has
/// been hidden a while) lets every view go.
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private readonly Dictionary<object, Control> _views = new();

    static PageHost() => PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.Follow());

    public object? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }

    private void Follow()
    {
        if (Page is null)
        {
            Children.Clear();
            _views.Clear();
            return;
        }
        foreach (var (page, view) in _views) view.IsVisible = ReferenceEquals(page, Page);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Page is { } page && !_views.ContainsKey(page) && (this.FindDataTemplate(page) ?? FuncDataTemplate.Default).Build(page) is { } view)
        {
            view.DataContext = page;
            _views[page] = view;
            Children.Add(view);
        }
        return base.MeasureOverride(availableSize);
    }
}
