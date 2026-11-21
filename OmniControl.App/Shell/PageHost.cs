using Avalonia.Controls.Templates;

namespace OmniControl.App.Shell;

/// The main window's page area. A page's view is built the first time the page is laid out (so a window started in the tray builds
/// nothing) and then kept with the window, also while it is hidden, so going back to a page or showing the window from the tray soon is
/// a visibility flip instead of a rebuild (the Resources page keeps its gadget tab the same way); the views go with the window when it is
/// let go after a long hide (<see cref="WindowLifecycle"/>). A hidden view runs nothing: the page stops its work when it leaves the screen
/// (<see cref="PageViewModel.OnOnScreenChanged"/>).
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private readonly Dictionary<object, Control> _views = new();

    static PageHost() => PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.Follow());

    public object? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }

    private void Follow()
    {
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
