namespace OmniControl.Core.Engine.Keyboard;

/// One key as the keyboard reports it: its virtual key, its scan code and whether it is an extended key.
public sealed record LearnedKey(ushort Vk, ushort Scan, bool Extended);

/// Learns one key by listening for it: a low-level keyboard hook on its own message-loop thread, installed only while it waits (the key is
/// never swallowed, and keys OmniControl itself sent are ignored). It answers with the first key pressed after a short settle time (so the
/// repeat of the key that started the learning is not taken), or null when none arrives in time or Windows refuses the hook.
public static class KeyLearner
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x100, WmSysKeyDown = 0x104, WmTimer = 0x113, WmNull = 0x0;
    private const uint LlkhfExtended = 0x1, LlkhfInjected = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData { public uint Vk, Scan, Flags, Time; public UIntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapsedMs, IntPtr proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, UIntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    /// Waits up to <paramref name="timeout"/> for a key; keys pressed in the first <paramref name="settle"/> are ignored.
    public static Task<LearnedKey?> LearnAsync(TimeSpan timeout, TimeSpan settle)
    {
        var done = new TaskCompletionSource<LearnedKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Listen(timeout, settle, done)) { IsBackground = true, Name = "Key learner" };
        thread.Start();
        return done.Task;
    }

    private static void Listen(TimeSpan timeout, TimeSpan settle, TaskCompletionSource<LearnedKey?> done)
    {
        var started = Environment.TickCount64;
        var thread = GetCurrentThreadId();
        LearnedKey? found = null;
        HookProc proc = (code, wParam, lParam) =>
        {
            if (code >= 0 && ((uint)wParam == WmKeyDown || (uint)wParam == WmSysKeyDown) && found is null && Environment.TickCount64 - started >= settle.TotalMilliseconds)
            {
                var data = Marshal.PtrToStructure<HookData>(lParam);
                if ((data.Flags & LlkhfInjected) == 0)
                {
                    found = new LearnedKey((ushort)data.Vk, (ushort)data.Scan, (data.Flags & LlkhfExtended) != 0);
                    PostThreadMessage(thread, WmNull, UIntPtr.Zero, IntPtr.Zero); // wakes the wait below, which sees the key
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        };
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            done.TrySetResult(null); // Windows refused the hook: the caller says the key cannot be copied
            return;
        }
        var timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, (uint)timeout.TotalMilliseconds, IntPtr.Zero);
        try
        {
            // Wakes on every message this thread gets (the hook's calls arrive as messages too) and ends on a key or the timer.
            while (found is null && GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
                if (m.Message == WmTimer) break;
        }
        finally
        {
            if (timer != UIntPtr.Zero) KillTimer(IntPtr.Zero, timer);
            UnhookWindowsHookEx(hook);
            GC.KeepAlive(proc); // the callback must outlive the hook
            done.TrySetResult(found);
        }
    }
}
