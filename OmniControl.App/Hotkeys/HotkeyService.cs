using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Win32.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Hotkeys;

/// <summary>Registers system-wide hotkeys (RegisterHotKey) on a dedicated message-loop thread.</summary>
public sealed class HotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312, WmApp = 0x8000, WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, UIntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly object _lock = new();
    private readonly Dictionary<int, Action> _active = new();
    private List<(string Gesture, Action Run)> _pending = new();
    private uint _threadId;

    public HotkeyService()
    {
        if (StartupMode.Inert) return; // no thread and no system hotkey in a screenshot export
        var ready = new ManualResetEventSlim();
        // Highest priority: the thread sleeps until a key is pressed and then must hand it on at once, whatever else is busy.
        new Thread(() => Loop(ready)) { IsBackground = true, Name = "Hotkeys", Priority = ThreadPriority.Highest }.Start();
        ready.Wait();
    }

    /// <summary>Replaces all registered hotkeys.</summary>
    public void Apply(IEnumerable<(string Gesture, Action Run)> bindings)
    {
        if (StartupMode.Inert) return;
        lock (_lock) _pending = bindings.ToList();
        PostThreadMessage(_threadId, WmApp, UIntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);

    private void Loop(ManualResetEventSlim ready)
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // creates this thread's message queue
        _threadId = GetCurrentThreadId();
        ready.Set();

        while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
        {
            if (m.Message == WmApp) Reregister();
            else if (m.Message == WmHotkey && _active.TryGetValue((int)m.WParam, out var run)) run();
        }
        foreach (var id in _active.Keys) UnregisterHotKey(IntPtr.Zero, id);
    }

    private void Reregister()
    {
        foreach (var id in _active.Keys) UnregisterHotKey(IntPtr.Zero, id);
        _active.Clear();

        List<(string Gesture, Action Run)> list;
        lock (_lock) list = _pending;

        var next = 1;
        foreach (var (gesture, run) in list)
        {
            if (TryParse(gesture, out var mod, out var vk) && RegisterHotKey(IntPtr.Zero, next, mod, vk))
                _active[next++] = run;
            else
                Failed?.Invoke(gesture);
        }
    }

    /// <summary>Raised (on the hotkey thread) when a gesture could not be registered, usually because another app owns it.</summary>
    public event Action<string>? Failed;

    private const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, ModWin = 8, VkF1 = 0x70, VkF24 = 0x87;

    /// <summary>
    /// Why a typed shortcut ("Ctrl+Alt+K") can't be registered, or null when it can: it must be readable and, like the Shortcuts
    /// page's boxes, hold Ctrl, Alt or Win unless it is an F key (a plain letter would break typing everywhere).
    /// </summary>
    public static string? ProblemWith(string gesture)
    {
        if (!TryParse(gesture.Trim(), out var mod, out var vk)) return "Couldn't read that shortcut. Use names like Ctrl+Alt+K or Win+F5.";
        return (mod & (ModAlt | ModCtrl | ModWin)) == 0 && vk is < VkF1 or > VkF24 ? "A shortcut needs Ctrl, Alt or Win, or an F key." : null;
    }

    private static bool TryParse(string gesture, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        var parts = gesture.Split('+');
        foreach (var p in parts[..^1])
            mod |= p switch { "Alt" => ModAlt, "Ctrl" => ModCtrl, "Shift" => ModShift, "Win" => ModWin, _ => 0u };
        if (!Enum.TryParse<Key>(parts[^1], out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }
}
