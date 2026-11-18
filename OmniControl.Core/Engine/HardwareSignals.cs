using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static OmniControl.Core.Engine.Trackpad.RawInputNative;
using static OmniControl.Core.Native.PowerNative;
using static OmniControl.Core.Native.SessionNative;
using static OmniControl.Core.Native.WindowNative;

namespace OmniControl.Core.Engine;

/// <summary>What Windows announced that may have changed the hardware the program sees. A watch lists the ones that can affect it.</summary>
[Flags]
public enum HardwareSignal
{
    None = 0,

    /// <summary>A device node came, went or changed (USB, Bluetooth, a touchpad...).</summary>
    Device = 1,

    /// <summary>The displays changed (one plugged in, removed, a resolution or layout change).</summary>
    Display = 2,

    /// <summary>A sound device was added, removed, enabled or disabled (Windows' audio notification; raised by the mixer's own watcher).</summary>
    Audio = 4,

    /// <summary>The user's session moved or came back: console connect (back at the PC after Remote Desktop), remote connect or disconnect, logon, unlock.</summary>
    Session = 8,

    /// <summary>The PC woke from sleep.</summary>
    Resume = 16,
}

/// <summary>
/// Raises <see cref="Raised"/> for Windows' hardware announcements that need no registration of a device kind: a device change, a display
/// change, a session that moved or came back (Remote Desktop, unlock) and a wake from sleep. They are only signals: they carry no detail and
/// arrive in bursts, so the one that listens decides what to look at again and when.
/// Two independent listeners, so one that Windows starves still leaves the other: a hidden top-level window on its own thread (broadcasts
/// such as device and display changes are not sent to message-only windows; it registers for the session notice itself) and .NET's
/// <see cref="SystemEvents"/> (its own window and thread). The same kind told by both is passed once (<see cref="HardwareSignalFilter"/>).
/// The same listeners also tell every power and session event, unfiltered and with the channel that heard it, through <see cref="Power"/>
/// (the power journal decides what is the same event); a third listener, a clock check (<see cref="ClockGap"/>), tells when the program was
/// stopped for a while, which catches a wake that sent no message.
/// </summary>
public sealed class HardwareSignals : IDisposable
{
    private const string ClassName = "OmniControlHardwareSignals";
    private const uint WmDeviceChange = 0x0219, WmDisplayChange = 0x007E;
    private const int DbtDevNodesChanged = 0x0007, DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;

    private readonly WndProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private readonly HardwareSignalFilter _filter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ClockGap _gap;
    private readonly TimeSpan _gapInterval;
    private readonly List<IntPtr> _powerNotices = new();
    private Timer? _clockCheck;
    private Thread? _thread;
    private IntPtr _window;

    /// <summary>Something announced a change, with where it came from for the log (raised on a listener's thread; handlers must return at once).</summary>
    public event Action<HardwareSignal, string>? Raised;

    /// <summary>A power or session event as one listener heard it (raised on that listener's thread; handlers must return at once).</summary>
    public event Action<PowerSignal>? Power;

    /// <summary>A listener could not start or a handler threw (text for the log).</summary>
    public event Action<string>? Failed;

    /// <param name="gapInterval">How often the clock check samples.</param>
    /// <param name="gapThreshold">How much later than asked a sample must come to count as a gap (see <see cref="ClockGap"/>).</param>
    public HardwareSignals(TimeSpan gapInterval, TimeSpan gapThreshold)
    {
        _proc = WindowProc;
        _gapInterval = gapInterval;
        _gap = new ClockGap((long)gapInterval.TotalMilliseconds, (long)gapThreshold.TotalMilliseconds);
    }

    /// <summary>Starts both listeners; false when Windows refused the window (<see cref="Failed"/> has the reason; <see cref="SystemEvents"/> may still run).</summary>
    public bool Start()
    {
        var started = false;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready, ok => started = ok)) { IsBackground = true, Name = "HardwareSignals" };
        _thread.Start();
        ready.Wait();
        StartSystemEvents();
        _clockCheck = new Timer(_ => SampleClock(), null, _gapInterval, _gapInterval);
        return started;
    }

    public void Dispose()
    {
        _clockCheck?.Dispose();
        _clockCheck = null;
        StopSystemEvents();
        var window = _window;
        if (window != IntPtr.Zero) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(CoreTiming.TrackpadStopWaitMs);
        _thread = null;
    }

    /// <summary>Tells the listeners once per kind (both listeners report the same Windows event).</summary>
    private void Raise(HardwareSignal signal, string source)
    {
        if (_filter.Pass(signal, _clock.ElapsedMilliseconds)) Raised?.Invoke(signal, source);
    }

    private void TellPower(PowerEventKind kind, PowerChannel channel, string detail) => Power?.Invoke(new PowerSignal(kind, channel, detail));

    // ---------------------------------------------------------------- third listener: the clock check

    /// <summary>Timer thread: one sample of the two clocks; a gap is told as a power event.</summary>
    private void SampleClock()
    {
        try
        {
            long? unbiased = QueryUnbiasedInterruptTime(out var time) ? (long)(time / 10_000) : null;
            if (_gap.Sample(Environment.TickCount64, unbiased) is { } signal) Power?.Invoke(signal);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A throw on a timer thread would end the process: it is reported and the check goes on at the next tick.
            Failed?.Invoke($"clock check failed: {ex}");
        }
    }

    // ---------------------------------------------------------------- second listener: .NET's SystemEvents

    private void StartSystemEvents()
    {
        try
        {
            // Subscribed from a thread without a message loop, so SystemEvents makes its own hidden window and thread.
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnded += OnSessionEnded;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            StopSystemEvents();
            Failed?.Invoke($"SystemEvents could not start ({ex.Message}); the hidden window alone listens.");
        }
    }

    private void StopSystemEvents()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnded -= OnSessionEnded;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect or SessionSwitchReason.RemoteDisconnect
            or SessionSwitchReason.SessionLogon or SessionSwitchReason.SessionUnlock)
            Guarded(() => Raise(HardwareSignal.Session, $"SystemEvents session {e.Reason}"));
        PowerEventKind? kind = e.Reason switch
        {
            SessionSwitchReason.SessionLock => PowerEventKind.Lock,
            SessionSwitchReason.SessionUnlock => PowerEventKind.Unlock,
            SessionSwitchReason.SessionLogon => PowerEventKind.Logon,
            SessionSwitchReason.SessionLogoff => PowerEventKind.Logoff,
            _ => null,
        };
        if (kind is { } k) Guarded(() => TellPower(k, PowerChannel.SystemEvents, $"session {e.Reason}"));
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e) =>
        Guarded(() => TellPower(e.Reason == SessionEndReasons.Logoff ? PowerEventKind.Logoff : PowerEventKind.Shutdown, PowerChannel.SystemEvents, $"session ended ({e.Reason})"));

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Guarded(() => Raise(HardwareSignal.Display, "SystemEvents display settings changed"));

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Guarded(() => Raise(HardwareSignal.Resume, "SystemEvents resume"));
        if (e.Mode is PowerModes.Resume or PowerModes.Suspend)
            Guarded(() => TellPower(e.Mode == PowerModes.Resume ? PowerEventKind.Resume : PowerEventKind.Suspend, PowerChannel.SystemEvents, $"power mode {e.Mode}"));
    }

    /// <summary>A throw on SystemEvents' thread would end the process: it is reported and the listener goes on.</summary>
    private void Guarded(Action raise)
    {
        try { raise(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed?.Invoke($"hardware listener (SystemEvents) failed: {ex}");
        }
    }

    // ---------------------------------------------------------------- first listener: the hidden window

    private void Loop(ManualResetEventSlim ready, Action<bool> started)
    {
        var instance = GetModuleHandle(null);
        var ok = Open(instance);
        started(ok);
        ready.Set(); // after this Start returns and disposes the event: it is not touched again
        if (ok)
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) DispatchMessage(ref msg);
        Close(instance);
    }

    private bool Open(IntPtr instance)
    {
        var wc = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), WndProc = Marshal.GetFunctionPointerForDelegate(_proc), Instance = instance, ClassName = ClassName };
        const int classExists = 1410;
        if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() is var error && error != classExists)
        {
            Failed?.Invoke($"Windows refused the hardware listener (error {error}).");
            return false;
        }
        // Parent zero = a top-level window (never HWND_MESSAGE), style 0 = never shown: it receives the display and device broadcasts.
        _window = CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            Failed?.Invoke($"Windows refused the hardware listener (error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        if (!WTSRegisterSessionNotification(_window, NotifyForThisSession))
            Failed?.Invoke($"Windows refused the session notice (error {Marshal.GetLastWin32Error()}); a return from Remote Desktop is then seen through SystemEvents and the display change.");
        foreach (var setting in new[] { ConsoleDisplayState, MonitorPowerOn })
        {
            var notice = RegisterForWindow(_window, setting);
            if (notice != IntPtr.Zero) _powerNotices.Add(notice);
            else Failed?.Invoke($"Windows refused the display power notice {setting} (error {Marshal.GetLastWin32Error()}); Modern Standby is then seen by the clock check.");
        }
        return true;
    }

    private void Close(IntPtr instance)
    {
        foreach (var notice in _powerNotices) UnregisterPowerSettingNotification(notice);
        _powerNotices.Clear();
        if (_window != IntPtr.Zero)
        {
            WTSUnRegisterSessionNotification(_window);
            DestroyWindow(_window); // no-op when WM_CLOSE already destroyed it
        }
        _window = IntPtr.Zero;
        UnregisterClass(ClassName, instance);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WmDeviceChange when (int)wParam is DbtDevNodesChanged or DbtDeviceArrival or DbtDeviceRemoveComplete:
                    Raise(HardwareSignal.Device, $"WM_DEVICECHANGE 0x{(int)wParam:X}");
                    break;
                case WmDisplayChange:
                    Raise(HardwareSignal.Display, "WM_DISPLAYCHANGE");
                    break;
                case WmPowerBroadcast:
                    OnPowerBroadcast((int)wParam, lParam);
                    break;
                case WmWtsSessionChange:
                    OnSessionChange((int)wParam);
                    break;
                case WmEndSession when wParam != IntPtr.Zero:
                    var logoff = (lParam.ToInt64() & EndSessionLogoff) != 0;
                    TellPower(logoff ? PowerEventKind.Logoff : PowerEventKind.Shutdown, PowerChannel.Window, "WM_ENDSESSION");
                    break;
                case WmClose:
                    DestroyWindow(hwnd);
                    _window = IntPtr.Zero;
                    return IntPtr.Zero;
                case WmDestroy:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception escaping a window procedure would take the whole app down; the failure is reported and the listener goes on.
            Failed?.Invoke($"hardware listener failed on message {message}: {ex}");
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnPowerBroadcast(int kind, IntPtr lParam)
    {
        switch (kind)
        {
            case PbtResumeSuspend or PbtResumeAutomatic:
                Raise(HardwareSignal.Resume, $"WM_POWERBROADCAST {kind}");
                TellPower(PowerEventKind.Resume, PowerChannel.Window, kind == PbtResumeAutomatic ? "PBT_APMRESUMEAUTOMATIC" : "PBT_APMRESUMESUSPEND");
                break;
            case PbtSuspend:
                TellPower(PowerEventKind.Suspend, PowerChannel.Window, "PBT_APMSUSPEND");
                break;
            case PbtPowerSettingChange when ReadSetting(lParam) is { } setting:
                var name = setting.Setting == ConsoleDisplayState ? "console display" : setting.Setting == MonitorPowerOn ? "monitor power" : null;
                // Dimmed (2) is neither: the display is still on.
                if (name is not null && setting.Value is 0 or 1)
                    TellPower(setting.Value == 0 ? PowerEventKind.DisplayOff : PowerEventKind.DisplayOn, PowerChannel.Display, $"{name} {(setting.Value == 0 ? "off" : "on")}");
                break;
        }
    }

    private void OnSessionChange(int kind)
    {
        if (kind is WtsConsoleConnect or WtsRemoteConnect or WtsRemoteDisconnect or WtsSessionLogon or WtsSessionUnlock)
            Raise(HardwareSignal.Session, $"WM_WTSSESSION_CHANGE {kind}");
        PowerEventKind? power = kind switch
        {
            WtsSessionLock => PowerEventKind.Lock,
            WtsSessionUnlock => PowerEventKind.Unlock,
            WtsSessionLogon => PowerEventKind.Logon,
            WtsSessionLogoff => PowerEventKind.Logoff,
            _ => null,
        };
        if (power is { } p) TellPower(p, PowerChannel.Window, $"WM_WTSSESSION_CHANGE {kind}");
    }
}
