using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// The lifecycle of a window that exists at most once (About, the app lists manager, the preset manager): shown through
/// <see cref="OwnedWindow"/>, brought to the front when asked for again, forgotten when it closes. A kept window (Actions and Hotkeys) is
/// only hidden when closed, so opening it again is instant; it lets go of its content while hidden, so it runs nothing, and of the window
/// itself when <see cref="Release"/>d.
public sealed class SingleWindow<T> where T : Window
{
    private readonly Action<string> _report;
    private readonly bool _keep;
    private T? _window; // from its making until it closes (a kept one also while hidden, or hidden with its owner)
    private bool _live; // its content is given and not yet let go
    private bool _closing; // Close(): a kept window really closes

    /// <param name="keep">Closing only hides the window, and the next <see cref="Show"/> shows the same one with fresh content.</param>
    public SingleWindow(Action<string> report, bool keep = false) => (_report, _keep) = (report, keep);

    /// The window while it is shown, else null.
    public T? Current => _window is { IsVisible: true } window ? window : null;

    /// Opens the window made by <paramref name="create"/>, or brings the open one to the front.
    /// <param name="closed">Runs once the window has closed, and for a kept one each time it is hidden (release what its view-model holds).</param>
    /// <param name="failure">Starts the footer sentence when the window cannot be opened.</param>
    /// <param name="opening">A kept window only: gives it fresh content before each showing, the first included (as a new window would have).</param>
    public void Show(Func<T> create, Action<T> closed, string failure, Action<T>? opening = null)
    {
        if (Current is { } open)
        {
            open.Activate();
            return;
        }
        var clock = Stopwatch.StartNew();
        try
        {
            var window = _window ??= Make(create, closed);
            if (_keep && !_live) opening?.Invoke(window);
            _live = true;
            UiTiming.WhenOpened(window, typeof(T).Name, clock);
            OwnedWindow.Show(window);
        }
        catch (Exception ex)
        {
            _report($"{failure}: {ex.Message}"); // a window problem must never take the program down
        }
    }

    private T Make(Func<T> create, Action<T> closed)
    {
        var window = create();
        void LetGo()
        {
            if (!_live) return;
            _live = false;
            closed(window);
        }
        window.Closed += (_, _) =>
        {
            _window = null;
            LetGo();
        };
        if (!_keep) return window;
        // Its X, Esc or Close button hides it; the app's exit and an owner that really closes still close it.
        window.Closing += (_, e) =>
        {
            if (_closing || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            window.Hide();
        };
        // Hidden by anything (its own close, or with the main window going to the tray): its content goes until it is shown again.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && !window.IsVisible) LetGo();
        };
        return window;
    }

    /// A kept window that is hidden closes for good, so its views go (the app sat in the tray a long while); the next <see cref="Show"/> makes a new one.
    public void Release()
    {
        if (_window is not { IsVisible: false } window) return;
        _closing = true;
        try { window.Close(); }
        finally { _closing = false; }
    }

    public void Close()
    {
        _closing = true;
        _window?.Close();
    }
}
