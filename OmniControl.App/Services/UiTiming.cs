using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OmniControl.App.Services;

/// <summary>
/// <c>timing-log.txt</c> in the data folder: how long the user waited for something to show (a window, a page, search results, the tray
/// panel), from the click or key to it being shown and to its first frame. A debugging aid, only in the log.
/// </summary>
public static class UiTiming
{
    private const string FileName = "timing-log.txt";

    /// <summary>Logs "<paramref name="what"/>: shown after N ms, first frame after M ms", N now and M once the frame is drawn.</summary>
    public static void Shown(string what, Stopwatch clock)
    {
        var shown = clock.ElapsedMilliseconds;
        Dispatcher.UIThread.Post(() => Write($"{what}: shown {shown} ms, first frame {clock.ElapsedMilliseconds} ms"), DispatcherPriority.Loaded);
    }

    /// <summary>Logs how long <paramref name="window"/> took from <paramref name="clock"/>'s start (the click) to opening and to its first frame.</summary>
    public static void WhenOpened(Window window, string what, Stopwatch clock)
    {
        void Opened(object? sender, EventArgs e)
        {
            window.Opened -= Opened;
            Shown($"Window {what}", clock);
        }
        window.Opened += Opened;
    }

    /// <summary>Writes one line (after the work it measures, so it never delays it).</summary>
    public static void Write(string line) => AppDataStore.AppendLog(FileName, line);
}
