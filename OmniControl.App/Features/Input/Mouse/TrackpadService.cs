using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Features.Input.Mouse;

/// The trackpad gestures: runs the Core touchpad watcher (only while a mapping, a rule or a page showing presses acts on a gesture) and
/// hands every recognised gesture to the shared mouse hook, which runs it like
/// a button press (custom rules first, then the mapping on the trackpad layer in force). Also
/// knows what the cards must say: whether a precision touchpad is there, whether the watcher could start, and which gestures Windows
/// itself also uses. While an action uses a gesture on any trackpad layer, Windows' own gesture is always turned off
/// (<see cref="GestureTakeover"/>, there is no switch for it) and given back when no action uses it, at exit, or at
/// the next start after a crash. A gesture whose action says "also let Windows do its own gesture" leaves Windows' one alone
/// (<see cref="MouseSettings.ReplacesWindows"/>). The takeover is kept by events, not only by a timer: any write to Windows' touchpad
/// settings key (the Settings app, a driver, Windows after resume, sign-in or a touchpad reconnect) is checked at once, a touchpad that
/// arrives makes the takeover apply again, and a look every <see cref="Timing.TrackpadTakeoverCheck"/> is the backstop (it also tells the
/// shell again when it still acts on its own old copy). Behind it, the safety net (<see cref="ShellSurfaceGuard"/>) closes a panel
/// Windows opened right after a gesture whose Windows action is supposed to be off.
public sealed class TrackpadService : IDisposable
{
    /// The entry in the master list of hardware watches: the touchpads and Windows' settings for them (the mouse hook needs no device list).
    public const string HardwareWatchId = "pointing.devices";

    private readonly MouseHook _hook;
    private readonly IHardwareWatches _hardware;
    private readonly MouseService _mouse;
    private readonly TrackpadLog _log = new();
    private readonly ITouchpadSettingsStore _store;
    private readonly GestureTakeover _takeover;
    private readonly ShellSurfaceGuard _guard;
    private readonly TouchpadSettingsWatcher _settingsWatcher;
    private readonly object _queueGate = new();
    private Task _queue = Task.CompletedTask; // takeover work runs here, one job at a time, off the UI thread (each write announces itself to every window)
    private readonly Func<bool> _needed;
    private TrackpadWatcher? _watcher;
    private string? _problem;

    /// Raised on the UI thread when what the cards show may have changed (a touchpad came or went, a takeover changed, Windows' settings).
    public event Action? Changed;

    /// Windows' own touchpad settings, as last read.
    public WindowsTouchpad Windows { get; private set; }

    /// True when Windows listed a precision touchpad at the last look.
    public bool IsPresent { get; private set; } = TrackpadWatcher.IsPresent();

    /// Why no trackpad gesture can be recognised now (one plain sentence), or null when they can.
    public string? UnavailableReason =>
        !IsPresent ? "No precision touchpad found; trackpad gestures need one."
        : !Windows.TouchpadOn ? "The touchpad is turned off in Windows settings."
        : _problem;

    /// <param name="hardware">The master list of hardware watches: the service registers the touchpads there and tells it when one comes or goes.</param>
    /// <param name="needed">Whether anything acts on a trackpad gesture now (a mapping, a rule, a page showing presses), asked from those owners; the
    /// watcher runs only then (<see cref="SyncWatcher"/>).</param>
    public TrackpadService(MouseHook hook, MouseService mouse, IHardwareWatches hardware, Func<bool> needed)
    {
        _store = new TouchpadSettingsStore(_log.Write);
        _hook = hook;
        _mouse = mouse;
        _hardware = hardware;
        _needed = needed;
        hardware.Register(new HardwareWatch(HardwareWatchId, "Mouse and touchpad", HardwareSignal.Device | HardwareSignal.Session | HardwareSignal.Resume,
            () => Dispatcher.UIThread.Invoke(Refresh), LooksStale: () => TrackpadWatcher.IsPresent() != IsPresent));
        Windows = WindowsTouchpad.Read(_store);
        _takeover = new GestureTakeover(_store, TrackpadBackup.Load(), TrackpadBackup.Save) { Log = _log.Write };
        _guard = new ShellSurfaceGuard(new ShellSurfaceProbe()) { Log = _log.Write };
        _settingsWatcher = new TouchpadSettingsWatcher(Timing.TrackpadTakeoverCheck) { Log = _log.Write };
        // A write to the key may have changed what the cards say even when no takeover changed, so the cards are told then too.
        _settingsWatcher.Changed += written => Enqueue(() => _takeover.Check() | written);
        _mouse.Applied += SyncTakeover;
        SyncWatcher();
        SyncTakeover(); // a crashed run's takeover that is no longer wanted is given back here
        _settingsWatcher.Start();
    }

    /// What OmniControl has done with this Windows gesture.
    public TakeoverStatus TakeoverOf(WindowsGesture gesture) => _takeover.StatusOf(gesture);

    /// Looks again at the touchpads and Windows' settings (the page was opened; the user may have changed them in Windows).
    public void Refresh()
    {
        IsPresent = TrackpadWatcher.IsPresent();
        Windows = WindowsTouchpad.Read(_store);
        Changed?.Invoke();
        SyncTakeover();
    }

    /// The Windows gestures to turn off: those of a gesture with an action (on any trackpad layer) that does not keep Windows' own.
    private IReadOnlySet<WindowsGesture> Wanted() => WantedOff(_mouse.Settings);

    /// The rule behind <see cref="Wanted"/>, apart so tests can check it: the takeover is always on for a remapped gesture.
    internal static IReadOnlySet<WindowsGesture> WantedOff(MouseSettings settings) =>
        TrackpadGestures.All.Where(g => settings.ReplacesWindows(g.Button)).Select(g => g.AlsoWindows).ToHashSet();

    /// True when the safety net watches this gesture on <paramref name="layer"/>: its first action there replaces Windows'
    /// gesture (not Default, "also let Windows do its own gesture" off), and none of its actions opens a Windows panel itself (Win+S,
    /// Alt+Tab...), which the net would otherwise take for Windows' own.
    internal static bool Guards(MouseSettings settings, int layer, MouseButton button) =>
        settings.ReplacesWindowsOn(layer, button) && !settings.OpensWindowsPanel(layer, button);

    /// Watcher thread: a gesture whose Windows action opens a panel and is supposed to be off was recognised: the safety net notes the
    /// window in front now (one quick look, before the gesture's own action runs) and watches for that panel for a moment, off this thread.
    private void GuardSurfaces(MouseButton button)
    {
        if (TrackpadGestures.Of(button) is not { Opens: not ShellSurface.None } gesture) return;
        if (!Guards(_mouse.Settings, _mouse.LayerOf(InputFamily.Trackpad), button) || !_guard.Arm(gesture.Name, gesture.Opens)) return;
        Guard.OffUi("Trackpad: watching the shell panels", async () =>
        {
            while (_guard.Look()) await Task.Delay(Timing.TrackpadSurfaceLook);
        });
    }

    /// UI thread: makes Windows' settings follow the mappings and switches, on the worker.
    private void SyncTakeover()
    {
        var wanted = Wanted();
        Enqueue(() =>
        {
            _takeover.Update(wanted);
            return true; // the cards' tags also depend on each action's settings, which may have changed without a status change
        });
    }

    /// Runs <paramref name="job"/> after the jobs before it, off the UI thread; when it reports a change the cards are told.
    private void Enqueue(Func<bool> job)
    {
        lock (_queueGate)
            _queue = _queue.ContinueWith(previous =>
            {
                if (previous.Exception is { } failed) _log.Write($"Touchpad settings job failed: {failed.InnerException?.Message}");
                if (job()) Dispatcher.UIThread.Post(AfterChange);
            }, TaskScheduler.Default);
    }

    private void AfterChange()
    {
        Windows = WindowsTouchpad.Read(_store);
        Changed?.Invoke();
    }

    /// Watcher thread: one rate-limited trackpad-log line per recognised gesture with Windows' matching setting read back now, both as
    /// stored and as Windows acts on it (the shell's own copy), so a gesture Windows still acted on can be matched to what its setting
    /// said at that moment. The reads and the write run off that thread.
    private void NoteGesture(MouseButton button)
    {
        if (TrackpadGestures.Of(button) is not { } gesture || !_log.TakeGestureLine()) return;
        Guard.OffUi("Trackpad: logging a gesture", () =>
        {
            var setting = WindowsGestureSettings.Of(gesture.AlsoWindows);
            var stored = Describe(setting, _store.Read(setting, out var value), value);
            var inEffect = Describe(setting, _store.ReadInEffect(setting, out var used), used);
            _log.Write($"{button} recognised; Windows' {setting.Name} ({setting.RegistryValue}) = {stored}, Windows acts on {inEffect}; "
                       + $"OmniControl: {_takeover.StatusOf(gesture.AlsoWindows).State}");
        });
    }

    private static string Describe(WindowsGestureSetting setting, string? problem, int? value) =>
        problem is not null ? $"unreadable ({problem})" : $"{GestureTakeover.Show(value)} ({(setting.IsOn(value) ? "Windows acts" : "Nothing")})";

    /// Starts the touchpad watcher while anything acts on a trackpad gesture and stops it otherwise (UI thread; the owners' changes call it).
    public void SyncWatcher()
    {
        if (_needed() == (_watcher is not null)) return;
        if (_watcher is null) StartWatcher();
        else StopWatcher();
        Changed?.Invoke();
    }

    private void StartWatcher()
    {
        var watcher = new TrackpadWatcher(Timing.TrackpadGestureCooldown) { Log = _log.Write };
        Subscribe(watcher);
        _problem = watcher.Start();
        if (_problem is null)
        {
            _watcher = watcher;
            return;
        }
        Unsubscribe(watcher);
        watcher.Dispose();
    }

    private void StopWatcher()
    {
        _problem = null; // off is not a failure, whether or not it had started
        if (_watcher is null) return;
        Unsubscribe(_watcher);
        _watcher.Dispose();
        _watcher = null;
    }

    // The safety net is armed first, so the window in front is noted before the gesture's own action runs.
    private void Subscribe(TrackpadWatcher watcher)
    {
        watcher.Recognised += GuardSurfaces;
        watcher.Recognised += _hook.Raise;
        watcher.Recognised += NoteGesture;
        watcher.DevicesChanged += OnDevicesChanged;
    }

    private void Unsubscribe(TrackpadWatcher watcher)
    {
        watcher.Recognised -= GuardSurfaces;
        watcher.Recognised -= _hook.Raise;
        watcher.Recognised -= NoteGesture;
        watcher.DevicesChanged -= OnDevicesChanged;
    }

    // A touchpad that arrives (or comes back after sleep) gets the takeover applied again through its hardware watch's refresh.
    private void OnDevicesChanged() => _hardware.Request(HardwareWatchId);

    /// At exit every Windows gesture OmniControl turned off is given back, on this thread, after the queued work.
    public void Dispose()
    {
        _settingsWatcher.Dispose();
        _mouse.Applied -= SyncTakeover;
        StopWatcher();
        Task pending;
        lock (_queueGate) pending = _queue;
        Task.WhenAny(pending).Wait(Timing.MouseQueueDrain); // WhenAny: a job that failed must not stop the give-back below
        _takeover.RestoreAll();
    }
}
