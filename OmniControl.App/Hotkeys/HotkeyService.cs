using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Win32.Input;

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
        var ready = new ManualResetEventSlim();
        new Thread(() => Loop(ready)) { IsBackground = true, Name = "Hotkeys" }.Start();
        ready.Wait();
    }

    /// <summary>Replaces all registered hotkeys.</summary>
    public void Apply(IEnumerable<(string Gesture, Action Run)> bindings)
    {
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

    private static bool TryParse(string gesture, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        var parts = gesture.Split('+');
        foreach (var p in parts[..^1])
            mod |= p switch { "Alt" => 1u, "Ctrl" => 2u, "Shift" => 4u, "Win" => 8u, _ => 0u };
        if (!Enum.TryParse<Key>(parts[^1], out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }
}
