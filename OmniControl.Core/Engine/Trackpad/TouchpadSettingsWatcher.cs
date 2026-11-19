using System.Security;
using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine.Trackpad;

/// Watches Windows' touchpad settings key (<c>HKCU\...\PrecisionTouchPad</c>, where the Settings app, SPI_SETTOUCHPADPARAMETERS and the
/// shell keep every gesture setting) on its own thread and raises <see cref="Changed"/>: at once (true) when any value under it is written,
/// whoever wrote it (the Settings app, a driver, Windows after resume, sign-in or a device reconnect, OmniControl itself), and every
/// interval (false) as a backstop for a change made without the registry. Nothing here writes. Dispose stops the thread and closes the key.
public sealed class TouchpadSettingsWatcher : IDisposable
{
    private const uint NotifyName = 0x1, NotifyLastSet = 0x4;

    private readonly TimeSpan _interval;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;

    /// Raised on the watcher's thread: true when the key was written, false for the regular look. Handlers must return at once.
    public event Action<bool>? Changed;

    /// Where the watcher says what it could not do (the key could not be watched). Never called per change.
    public Action<string>? Log { get; init; }

    /// <param name="interval">The backstop: how often <see cref="Changed"/> is raised when nothing was written.</param>
    public TouchpadSettingsWatcher(TimeSpan interval) => _interval = interval;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "TouchpadSettingsWatcher" };
        _thread.Start();
    }

    private void Loop()
    {
        using var written = new AutoResetEvent(false);
        RegistryKey? key = null;
        var reported = false;
        try
        {
            while (true)
            {
                // The notification is one-shot and tied to this thread, so it is asked for again before every wait.
                key ??= Open();
                var armed = key is not null && Arm(key, written);
                if (!armed)
                {
                    key?.Dispose(); // a key that was deleted is opened again on the next look
                    key = null;
                    if (!reported) Log?.Invoke("touchpad settings key can't be watched; looking every few seconds instead");
                    reported = true;
                }
                var woke = WaitHandle.WaitAny(new WaitHandle[] { _stop, written }, _interval);
                if (woke == 0) return;
                Changed?.Invoke(woke == 1);
            }
        }
        finally
        {
            key?.Dispose();
        }
    }

    private static RegistryKey? Open()
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey(WindowsTouchpad.Key); // read access includes the right to be told of changes
        }
        catch (Exception e) when (e is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null; // tried again on the next look
        }
    }

    private static bool Arm(RegistryKey key, AutoResetEvent written) =>
        RegNotifyChangeKeyValue(key.Handle, true, NotifyName | NotifyLastSet, written.SafeWaitHandle, true) == 0;

    public void Dispose()
    {
        _stop.Set();
        var ended = _thread?.Join(CoreTiming.TrackpadStopWaitMs) ?? true;
        _thread = null;
        if (ended) _stop.Dispose(); // a thread still waiting on it would fail on a closed handle; it ends with the program instead
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter, SafeWaitHandle evt, bool asynchronous);
}
