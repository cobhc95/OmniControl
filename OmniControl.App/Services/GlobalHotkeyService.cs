using System.Runtime.InteropServices;
using OmniControl.App.Models;

namespace OmniControl.App.Services;

/// <summary>Registers global hotkeys via user32 RegisterHotKey on a hidden message-only window. Windows only.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style; public WndProcDelegate lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName; public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASS c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    private readonly WndProcDelegate _proc;
    private readonly Dictionary<int, HotkeyBinding> _registered = new();
    private IntPtr _hwnd;
    private readonly Action<HotkeyBinding> _onHotkey;

    public GlobalHotkeyService(Action<HotkeyBinding> onHotkey)
    {
        _onHotkey = onHotkey;
        _proc = WndProc;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var inst = GetModuleHandleW(null);
            var cls = new WNDCLASS { lpfnWndProc = _proc, hInstance = inst, lpszClassName = "OmniControlHotkeyWnd" };
            RegisterClassW(ref cls);
            _hwnd = CreateWindowExW(0, cls.lpszClassName, "OmniControlHotkeys", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, inst, IntPtr.Zero);
        }
        catch { _hwnd = IntPtr.Zero; }
    }

    public bool IsAvailable => _hwnd != IntPtr.Zero;

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(w.ToInt32(), out var b))
        {
            try { _onHotkey(b); } catch { }
            return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    /// <summary>Re-registers all enabled Global bindings that have a command. Failures are reported via log.</summary>
    public void Apply(IEnumerable<HotkeyBinding> bindings, Action<string> log)
    {
        UnregisterAll();
        if (!IsAvailable) return;
        var seen = new HashSet<(HotkeyMods, int)>();
        int id = 1;
        foreach (var b in bindings)
        {
            if (!b.Enabled || b.Scope != HotkeyScope.Global || string.IsNullOrWhiteSpace(b.Command)) continue;
            if (!seen.Add((b.Modifiers, b.Key))) { log($"Hotkey '{b.Action}' skipped: keys already used by another binding."); continue; }
            if (RegisterHotKey(_hwnd, id, (uint)b.Modifiers | MOD_NOREPEAT, (uint)b.Key)) _registered[id++] = b;
            else log($"Hotkey '{b.Action}' could not be registered (key in use by another app, error {Marshal.GetLastWin32Error()}).");
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterAll();
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
