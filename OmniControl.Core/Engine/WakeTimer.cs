using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine;

/// One Windows wake timer: while set, Windows wakes the PC from sleep or hibernation at that time. It cannot start a PC that is turned
/// off. Nothing runs when it fires; the caller's own schedule does the work once the PC is awake.
public sealed class WakeTimer : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle CreateWaitableTimer(IntPtr attributes, bool manualReset, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelWaitableTimer(SafeWaitHandle timer);

    private SafeWaitHandle? _timer;

    /// Wakes the PC at <paramref name="when"/> (local time), replacing an earlier time. False when Windows refused (wake timers off).
    public bool Set(DateTime when)
    {
        _timer ??= CreateWaitableTimer(IntPtr.Zero, true, null);
        if (_timer.IsInvalid) return false;
        var due = when.ToFileTimeUtc(); // a positive value is an absolute time
        return SetWaitableTimer(_timer, due, 0, IntPtr.Zero, IntPtr.Zero, resume: true);
    }

    public void Cancel()
    {
        if (_timer is { IsInvalid: false }) CancelWaitableTimer(_timer);
    }

    public void Dispose()
    {
        Cancel();
        _timer?.Dispose();
        _timer = null;
    }
}
