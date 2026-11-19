using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// <c>timing-log.txt</c> in the data folder: how long the user waited for something to show (a window, a page, search results, the tray
/// panel), from the click or key to it being shown and to its first frame. A debugging aid, only in the log.
public static class UiTiming
{
    private const string FileName = "timing-log.txt";

    /// Logs "<paramref name="what"/>: shown after N ms, first frame after M ms", N now and M once the frame is drawn.
    public static void Shown(string what, Stopwatch clock)
    {
        var shown = clock.ElapsedMilliseconds;
        Dispatcher.UIThread.Post(() => Write($"{what}: shown {shown} ms, first frame {clock.ElapsedMilliseconds} ms"), DispatcherPriority.Loaded);
    }

    /// Logs how long <paramref name="window"/> took from <paramref name="clock"/>'s start (the click) to opening and to its first frame.
    public static void WhenOpened(Window window, string what, Stopwatch clock)
    {
        void Opened(object? sender, EventArgs e)
        {
            window.Opened -= Opened;
            Shown($"Window {what}", clock);
        }
        window.Opened += Opened;
    }

    /// Writes one line (after the work it measures, so it never delays it).
    public static void Write(string line) => AppDataStore.AppendLog(FileName, line);
}
