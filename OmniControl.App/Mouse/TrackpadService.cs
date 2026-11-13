using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Mouse;

/// <summary>
/// The trackpad gestures: runs the Core touchpad watcher while "OmniControl trackpad gestures" is on and hands every recognised gesture to the
/// shared mouse hook, which runs it like a button press (custom rules first, then the mapping on the layer in force). Also knows
/// what the cards must say: whether a precision touchpad is there, whether the watcher could start, and which gestures Windows
/// itself also uses (read only; OmniControl never changes Windows' touchpad settings).
/// </summary>
public sealed class TrackpadService : IDisposable
{
    private const string LogFile = "mouse-log.txt";

    private readonly MouseHook _hook;
    private readonly MouseService _mouse;
    private TrackpadWatcher? _watcher;
    private string? _problem;

    /// <summary>Raised on the UI thread when what the cards show may have changed (a touchpad came or went, the switch, Windows' settings).</summary>
    public event Action? Changed;

    /// <summary>Windows' own touchpad settings, as last read.</summary>
    public WindowsTouchpad Windows { get; private set; } = WindowsTouchpad.Read();

    /// <summary>True when Windows listed a precision touchpad at the last look.</summary>
    public bool IsPresent { get; private set; } = TrackpadWatcher.IsPresent();

    public bool IsEnabled => _mouse.Settings.TrackpadEnabled;

    /// <summary>Why no trackpad gesture can be recognised now (one plain sentence), or null when they can.</summary>
    public string? UnavailableReason =>
        !IsPresent ? "No precision touchpad found. Trackpad gestures need a precision touchpad."
        : !Windows.TouchpadOn ? "The touchpad is turned off in Windows settings."
        : _problem;

    public TrackpadService(MouseHook hook, MouseService mouse)
    {
        _hook = hook;
        _mouse = mouse;
        if (IsEnabled) StartWatcher();
    }

    public void SetEnabled(bool on)
    {
        if (on == IsEnabled) return;
        _mouse.Settings.TrackpadEnabled = on;
        _mouse.Apply();
        if (on) StartWatcher();
        else StopWatcher();
        Changed?.Invoke();
    }

    /// <summary>Looks again at the touchpads and Windows' settings (the page was opened; the user may have changed them in Windows).</summary>
    public void Refresh()
    {
        IsPresent = TrackpadWatcher.IsPresent();
        Windows = WindowsTouchpad.Read();
        Changed?.Invoke();
    }

    private void StartWatcher()
    {
        var watcher = new TrackpadWatcher(Timing.TrackpadGestureCooldown) { Log = line => AppDataStore.AppendLog(LogFile, line) };
        watcher.Recognised += _hook.Raise;
        watcher.DevicesChanged += OnDevicesChanged;
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

    private void Unsubscribe(TrackpadWatcher watcher)
    {
        watcher.Recognised -= _hook.Raise;
        watcher.DevicesChanged -= OnDevicesChanged;
    }

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Dispose() => StopWatcher();
}
