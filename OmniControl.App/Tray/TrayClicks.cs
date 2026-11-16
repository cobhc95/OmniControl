using Avalonia.Threading;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Tray;

/// <summary>
/// Tells a single click on the tray icon from a double click. A first click waits Windows' own double-click time (the user's mouse setting,
/// <see cref="SystemCommands.DoubleClick"/>, read at each click) for a second one: a second click within it is a double click and the
/// single click never fires; none, and the single click fires when the time is up.
/// </summary>
internal sealed class TrayClicks : IDisposable
{
    private readonly DispatcherTimer _wait = new();
    private readonly Action _single, _double;

    public TrayClicks(Action single, Action @double)
    {
        (_single, _double) = (single, @double);
        _wait.Tick += OnWaited;
    }

    public void Click()
    {
        if (_wait.IsEnabled)
        {
            _wait.Stop();
            _double();
            return;
        }
        _wait.Interval = TimeSpan.FromMilliseconds(SystemCommands.DoubleClick().Milliseconds);
        _wait.Start();
    }

    private void OnWaited(object? sender, EventArgs e)
    {
        _wait.Stop();
        _single();
    }

    public void Dispose()
    {
        _wait.Stop();
        _wait.Tick -= OnWaited;
    }
}
