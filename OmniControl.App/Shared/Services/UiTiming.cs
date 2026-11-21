using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Services;

/// <c>timing-log.txt</c> in the data folder: how long the user waited for something to show (a window, a page, search results, the tray
/// panel), from the click or key to it being shown and to its first frame, and the process's memory at a few quiet moments. A debugging aid, only in the log.
public static class UiTiming
{
    /// Logs the process's memory <paramref name="when"/>, read on a worker (the process figures come from Windows' process list): the
    /// managed heap after the last collection (size, fragmented, committed), the live managed bytes, private bytes and working set, then
    /// the visuals in the open windows (counted here, on the UI thread) and <paramref name="note"/>. Call on the UI thread.
    public static void Memory(string when, string? note = null)
    {
        var visuals = Visuals();
        Guard.OffUi("Memory line", () =>
        {
            var gc = GC.GetGCMemoryInfo();
            using var self = Process.GetCurrentProcess();
            Write($"Memory {when}: heap {Mb(gc.HeapSizeBytes)}, fragmented {Mb(gc.FragmentedBytes)}, committed {Mb(gc.TotalCommittedBytes)}, "
                + $"live {Mb(GC.GetTotalMemory(false))}, private {Mb(self.PrivateMemorySize64)}, working set {Mb(self.WorkingSet64)} (MB); {visuals}"
                + (note is null ? string.Empty : $"; {note}"));
        });
    }

    /// Every window the app has open (shown or hidden), with all its visuals: what the main window's release should take away. Call on the UI thread.
    public static string Visuals()
    {
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
        return $"{windows.Sum(w => 1 + w.GetVisualDescendants().Count())} visuals in {windows.Count} windows";
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private const string FileName = LogFiles.Timing;

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
