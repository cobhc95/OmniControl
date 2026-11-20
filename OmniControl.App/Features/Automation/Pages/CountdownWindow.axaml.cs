using Avalonia.Input;

namespace OmniControl.App.Features.Automation.Pages;

/// The window of the cancellable countdown before a power action. Closing it any other way than "Do it now" or the end of the time
/// cancels. It has no owner (the main window may be in the tray) and stays on top.
public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = Timing.CountdownTick };
    private TimeSpan _left;
    private bool _go;

    public CountdownWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
    }

    /// Shows the countdown; true when the time ran out or "Do it now" was pressed, false when it was cancelled (here or through <paramref name="cancel"/>).
    public static Task<bool> ConfirmAsync(string title, string text, TimeSpan time, CancellationToken cancel)
    {
        var window = new CountdownWindow { Title = title, _left = time };
        window.TitleText.Text = title;
        window.BodyText.Text = text;
        window.ShowSeconds();
        window._timer.Start();
        var done = new TaskCompletionSource<bool>();
        var cancelled = cancel.Register(() => Dispatcher.UIThread.Post(window.Close));
        window.Closed += (_, _) =>
        {
            cancelled.Dispose();
            done.TrySetResult(window._go);
        };
        window.Show();
        return done.Task;
    }

    private void Tick()
    {
        _left -= _timer.Interval;
        if (_left <= TimeSpan.Zero)
        {
            _go = true;
            Close();
            return;
        }
        ShowSeconds();
    }

    private void ShowSeconds() => SecondsText.Text = $"{Math.Ceiling(_left.TotalSeconds):0} s left";

    private void OnNowClick(object? sender, RoutedEventArgs e)
    {
        _go = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
