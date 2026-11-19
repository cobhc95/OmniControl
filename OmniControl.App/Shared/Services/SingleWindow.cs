using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// The lifecycle of a window that exists at most once (About, the app lists manager, the preset manager): shown through
/// <see cref="OwnedWindow"/>, brought to the front when asked for again, forgotten when it closes.
public sealed class SingleWindow<T> where T : Window
{
    private readonly Action<string> _report;

    public SingleWindow(Action<string> report) => _report = report;

    /// The window while it is open, else null.
    public T? Current { get; private set; }

    /// Opens the window made by <paramref name="create"/>, or brings the open one to the front.
    /// <param name="closed">Runs once the window has closed (release what its view-model holds).</param>
    /// <param name="failure">Starts the footer sentence when the window cannot be opened.</param>
    public void Show(Func<T> create, Action<T> closed, string failure)
    {
        if (Current is { } open)
        {
            open.Activate();
            return;
        }
        var clock = Stopwatch.StartNew();
        try
        {
            var window = create();
            UiTiming.WhenOpened(window, typeof(T).Name, clock);
            window.Closed += (_, _) =>
            {
                Current = null;
                closed(window);
            };
            Current = window;
            OwnedWindow.Show(window);
        }
        catch (Exception ex)
        {
            Current = null;
            _report($"{failure}: {ex.Message}"); // a window problem must never take the program down
        }
    }

    public void Close() => Current?.Close();
}
