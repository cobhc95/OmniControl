using OmniControl.App.Services;
using OmniControl.Core.Engine;
using Avalonia.Threading;

namespace OmniControl.App.Osd;

/// <summary>
/// Best-effort suppression of Windows' own volume/brightness pop-up (Windows 11): its window is minimised, and Windows then never shows it.
/// Explorer uses the same kind of window for other pop-ups, the taskbar's thumbnail previews among them (<see cref="WindowsOsdWindows"/>), and
/// minimising those broke the taskbar while OmniControl ran. So a window is taken for the volume pop-up only when it opens within
/// <see cref="Timing.WindowsOsdMatch"/> of a volume, brightness, microphone or media change made outside OmniControl (<see cref="Trigger"/>)
/// while the pointer is not on the taskbar; one Windows opens again with no such change near it is let go. Nothing here activates a window
/// or waits on Explorer. Found windows are logged to %APPDATA%\OmniControl\osd-log.txt.
/// The window-finding approach follows the open-source HideVolumeOSD tool (gpkgpk/HideVolumeOSD, tag v1.3.3.0).
/// </summary>
public sealed class WindowsOsd : IDisposable
{
    /// <summary>How many times the pop-up is shown (a mute pair) to find its window before giving up (a gap of <see cref="Timing.WindowsOsdRetry"/> between tries).</summary>
    private const int MaxCreateTries = 5;

    /// <summary>Lines written to osd-log.txt per run, so a stuck loop cannot grow the file.</summary>
    private const int MaxLogLines = 100;

    private readonly DispatcherTimer _timer = new() { Interval = Timing.WindowsOsdPoll };
    private readonly Dictionary<IntPtr, long> _shownSince = new(); // each candidate on screen now, and since when (tick count)
    private readonly HashSet<IntPtr> _osd = new();                 // the windows taken for Windows' pop-up and minimised here
    private long _lastChange = long.MinValue / 2;                  // tick count of the last change Windows may show its pop-up for
    private DateTime _lastCreate = DateTime.MinValue;
    private int _creates;
    private int _logged;

    public WindowsOsd() => _timer.Tick += (_, _) => Tick();

    public bool Hidden
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled) return;
            if (value)
            {
                _creates = 0;
                RestoreStrays();
                _timer.Start();
            }
            else { _timer.Stop(); Restore(); }
        }
    }

    /// <summary>
    /// A change Windows may show its pop-up for happened just now (volume, brightness, microphone or media, made outside OmniControl).
    /// Any thread.
    /// </summary>
    public void Trigger() => Interlocked.Exchange(ref _lastChange, Environment.TickCount64);

    private void Tick()
    {
        var now = Environment.TickCount64;
        var hosts = WindowsOsdWindows.Find();
        Forget(hosts);
        if (_osd.Count == 0) Probe();

        var nearChange = Math.Abs(now - Interlocked.Read(ref _lastChange)) <= Timing.WindowsOsdMatch.TotalMilliseconds;
        foreach (var host in hosts)
        {
            if (!WindowsOsdWindows.IsShown(host))
            {
                _shownSince.Remove(host);
                continue;
            }
            if (!_shownSince.TryGetValue(host, out var since)) _shownSince[host] = since = now;
            var justOpened = now - since <= Timing.WindowsOsdMatch.TotalMilliseconds;
            if (_osd.Contains(host))
            {
                if (justOpened && nearChange) Minimise(host, "again");
                else if (!justOpened)
                {
                    _osd.Remove(host);
                    Log($"let go of {WindowsOsdWindows.Describe(host)}: Windows showed it with no volume, brightness or media change near, so it is not the volume pop-up");
                }
                continue;
            }
            if (justOpened && nearChange && !WindowsOsdWindows.PointerOverShell())
            {
                _osd.Add(host);
                Minimise(host, $"opened {now - Interlocked.Read(ref _lastChange)} ms after a change");
            }
        }
    }

    /// <summary>
    /// While the pop-up's window is not known, it is shown once (a mute pair, at most <see cref="MaxCreateTries"/> times) so the window that
    /// opens can be told from Explorer's other pop-ups.
    /// </summary>
    private void Probe()
    {
        if (_creates >= MaxCreateTries || Grace.Within(_lastCreate, Timing.WindowsOsdRetry)) return;
        _lastCreate = DateTime.UtcNow;
        if (++_creates == MaxCreateTries) Log("last try to find Windows' pop-up; if it is not found it is left alone (shown)");
        Log("Windows' pop-up not known yet, sending mute pair");
        Trigger();
        WindowsOsdWindows.ShowVolumePopup();
    }

    private void Minimise(IntPtr host, string why)
    {
        Log($"minimising {WindowsOsdWindows.Describe(host)} ({why})");
        WindowsOsdWindows.Minimise(host);
    }

    /// <summary>Drops what is kept about windows that no longer exist (Explorer restarted or closed a pop-up for good).</summary>
    private void Forget(List<IntPtr> hosts)
    {
        foreach (var gone in _shownSince.Keys.Where(h => !hosts.Contains(h)).ToList()) _shownSince.Remove(gone);
        _osd.RemoveWhere(h => !hosts.Contains(h));
    }

    /// <summary>
    /// Explorer never minimises these pop-ups itself, so a minimised one when hiding starts was left so by an earlier run (an earlier version
    /// also minimised the taskbar's thumbnail previews): it is put back, without activating it. The first probe then waits a retry gap, so
    /// a window put back here is never taken for the pop-up as it reappears.
    /// </summary>
    private void RestoreStrays()
    {
        foreach (var host in WindowsOsdWindows.Find().Where(WindowsOsdWindows.IsMinimised))
        {
            Log($"putting back {WindowsOsdWindows.Describe(host)}, left minimised by an earlier run");
            WindowsOsdWindows.Restore(host);
            _lastCreate = DateTime.UtcNow;
        }
    }

    /// <summary>Brings back Windows' own pop-up: puts back every minimised one (without activating it), then shows the pop-up once so Windows picks it up again.</summary>
    public void Restore()
    {
        var any = false;
        foreach (var host in WindowsOsdWindows.Find().Where(WindowsOsdWindows.IsMinimised))
        {
            WindowsOsdWindows.Restore(host);
            any = true;
        }
        _osd.Clear();
        _shownSince.Clear();
        if (any) { Log("restored OSD windows"); WindowsOsdWindows.ShowVolumePopup(); }
    }

    private void Log(string line)
    {
        if (_logged++ > MaxLogLines) return;
        AppDataStore.AppendLog("osd-log.txt", line);
    }

    public void Dispose() => Hidden = false;
}
