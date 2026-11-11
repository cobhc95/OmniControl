using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// Turns key presses in a hotkey box into a binding. Pure hotkey logic: it knows nothing about cards, lists or focus movement
/// (those live in <c>Pages/RowEditing.cs</c>, which calls this when the focused control is a hotkey box).
/// </summary>
public static class HotkeyCapture
{
    /// <summary>True while a hotkey box waits for a key combination, so Esc and Ctrl+F belong to it.</summary>
    public static bool IsListening(object? focused) =>
        focused is TextBox { DataContext: HotkeyAction { IsListening: true } };

    /// <summary>True for the read-only box that shows a hotkey.</summary>
    public static bool IsHotkeyBox(object? control) =>
        control is TextBox { IsReadOnly: true, DataContext: HotkeyAction };

    /// <summary>Handles one key press in a hotkey box. Returns true when the key was used.</summary>
    public static bool TryHandle(KeyEventArgs e, HotkeyAction action)
    {
        var m = e.KeyModifiers;
        if (e.Key is Key.Back or Key.Delete && m == KeyModifiers.None)
        {
            action.Gesture = string.Empty;
            action.IsListening = false;
            return true;
        }
        if (!action.IsListening)
        {
            if (e.Key == Key.Enter && m == KeyModifiers.None) { action.IsListening = true; return true; }
            return false;
        }
        if (e.Key is Key.Escape or Key.Enter && m == KeyModifiers.None)
        {
            action.IsListening = false; // cancel, keeping the old binding
            return true;
        }

        var isModifierKey = e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
        if (isModifierKey) return false;
        var isFunctionKey = e.Key is >= Key.F1 and <= Key.F24;
        if ((m & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0 && !isFunctionKey)
        {
            action.RejectedNote = true; // plain letters would break typing everywhere
            return true;
        }

        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(e.Key.ToString());
        action.Gesture = string.Join("+", parts);
        action.IsListening = false;
        return true;
    }
}
