using System.Runtime.InteropServices;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine.Keyboard;

/// <summary>
/// Takes one Windows-key shortcut (Win plus a letter) from Windows: a low-level keyboard hook on its own message-loop thread that swallows the
/// letter's press (and its repeats) and release and raises <see cref="Pressed"/> once per press. The Win key itself always passes through, so it
/// can never stay down; because Windows would then open the Start menu when Win is let go alone, one unassigned key is tapped (down and up in one
/// call) at the moment the letter is swallowed. Keys OmniControl sent itself are ignored. The callback only classifies; the answer is raised on the pool.
/// </summary>
public sealed class WinKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105, WmQuit = 0x12;
    private const uint LlkhfInjected = 0x10;
    private const ushort VkLWin = 0x5B, VkRWin = 0x5C, VkUnassigned = 0xFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData { public uint Vk, Scan, Flags, Time; public UIntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, UIntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private readonly uint _vk;
    private uint _thread;
    private bool _winDown, _swallowing;

    /// <param name="letterVk">The virtual key of the letter taken with Win (0x41 = A).</param>
    public WinKeyHook(ushort letterVk) => _vk = letterVk;

    /// <summary>Raised on a pool thread each time Win+letter is pressed (not for its repeats).</summary>
    public event Action? Pressed;

    /// <summary>Installs the hook; false when Windows refuses it. Does nothing when it is already running.</summary>
    public bool Start()
    {
        if (_thread != 0) return true;
        var ready = new ManualResetEventSlim();
        var installed = false;
        new Thread(() => Loop(ready, ok => installed = ok)) { IsBackground = true, Name = "Win key hook" }.Start();
        ready.Wait();
        return installed;
    }

    /// <summary>Removes the hook; keys then reach Windows as usual.</summary>
    public void Stop()
    {
        var thread = _thread;
        _thread = 0;
        if (thread != 0) PostThreadMessage(thread, WmQuit, UIntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();

    private void Loop(ManualResetEventSlim ready, Action<bool> report)
    {
        var thread = GetCurrentThreadId();
        _winDown = _swallowing = false;
        HookProc proc = Filter;
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, GetModuleHandle(null), 0);
        if (hook != IntPtr.Zero) _thread = thread;
        report(hook != IntPtr.Zero);
        ready.Set();
        if (hook == IntPtr.Zero) return;
        try
        {
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
            GC.KeepAlive(proc); // the callback must outlive the hook
        }
    }

    private IntPtr Filter(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<HookData>(lParam);
            if ((data.Flags & LlkhfInjected) == 0 && Swallows((uint)wParam, data.Vk)) return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool Swallows(uint message, uint vk)
    {
        var down = message is WmKeyDown or WmSysKeyDown;
        var up = message is WmKeyUp or WmSysKeyUp;
        if (vk is VkLWin or VkRWin)
        {
            _winDown = down; // the Win key itself is never taken
            return false;
        }
        if (vk != _vk) return false;
        if (down && (_winDown || _swallowing))
        {
            if (!_swallowing)
            {
                _swallowing = true;
                // The unassigned tap (so letting go of Win alone does not open the Start menu) is sent from a worker: input sent from inside
                // the hook callback waits on this very hook and can stall all typing until Windows drops the hook.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    InputSender.Tap(VkUnassigned, 0, false);
                    Pressed?.Invoke();
                });
            }
            return true;
        }
        if (up && _swallowing)
        {
            _swallowing = false;
            return true;
        }
        return false;
    }
}
