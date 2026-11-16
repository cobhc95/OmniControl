using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// Turns key presses in a hotkey box into a binding. Pure hotkey logic: it knows nothing about cards, lists or focus movement
/// (those live in <c>Pages/RowEditing.cs</c>, which calls this when the focused control is a hotkey box). The box previews the
/// keys held ("Ctrl + Alt + ..."); the shortcut is saved when its main key is released, so a combination is never cut short.
/// </summary>
public static class HotkeyCapture
{
    /// <summary>Stands for the main key still to press in a preview.</summary>
    public const string Ellipsis = "…";

    /// <summary>True while a hotkey box waits for a key combination, so Esc and Ctrl+F belong to it.</summary>
    public static bool IsListening(object? focused) =>
        focused is TextBox { DataContext: HotkeyTarget { IsListening: true } };

    /// <summary>True for the read-only box that shows a hotkey.</summary>
    public static bool IsHotkeyBox(object? control) =>
        control is TextBox { IsReadOnly: true, DataContext: HotkeyTarget };

    /// <summary>A press anywhere but in the hotkey box that is listening cancels listening (the old key stays).</summary>
    public static void CancelOnPressElsewhere(object? focused, object? pressed)
    {
        if (focused is TextBox { DataContext: HotkeyTarget { IsListening: true } target } box
            && !(pressed is Avalonia.Visual v && (v == box || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(box, v))))
            target.IsListening = false;
    }

    /// <summary>Handles one key press in a hotkey box. Returns true when the key was used.</summary>
    public static bool TryHandle(KeyEventArgs e, HotkeyTarget action)
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

        if (ModifierOf(e.Key) != KeyModifiers.None)
        {
            if (action.PendingKey is null) action.Preview = Preview(m, null);
            return true;
        }
        var isFunctionKey = e.Key is >= Key.F1 and <= Key.F24;
        if ((m & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0 && !isFunctionKey)
        {
            action.RejectedNote = true; // plain letters would break typing everywhere
            return true;
        }

        action.RejectedNote = false;
        action.PendingGesture = string.Join("+", Parts(m, e.Key.ToString()));
        action.PendingKey = (int)e.Key;
        action.Preview = Preview(m, e.Key.ToString());
        return true;
    }

    /// <summary>Handles one key release in a hotkey box: the main key saves the shortcut, a modifier updates the preview. True when used.</summary>
    public static bool TryHandleUp(KeyEventArgs e, HotkeyTarget action)
    {
        if (!action.IsListening) return false;
        if (ModifierOf(e.Key) is var released and not KeyModifiers.None)
        {
            if (action.PendingKey is null) action.Preview = Preview(e.KeyModifiers & ~released, null);
            return true;
        }
        if (action.PendingKey != (int)e.Key || action.PendingGesture is not { } gesture) return false;
        action.IsListening = false;
        action.Capture(gesture);
        return true;
    }

    // Chords a text box uses itself (select all, copy, paste, undo, move by word...), so the search box keeps them as editing keys.
    private static readonly Key[] EditingKeys = { Key.A, Key.C, Key.V, Key.X, Key.Z, Key.Y, Key.Left, Key.Right, Key.Back, Key.Delete, Key.Home, Key.End };

    /// <summary>
    /// The combination a key press in a search box stands for ("Ctrl+O"), written as a binding is, so the actions on it can be found: a key
    /// with Ctrl, Alt or Win, or an F key or Print Screen alone. Null for typing, a modifier on its own, Ctrl+F and the text box's editing keys.
    /// </summary>
    public static string? SearchChordOf(KeyEventArgs e)
    {
        var m = e.KeyModifiers;
        if (ModifierOf(e.Key) != KeyModifiers.None) return null;
        var withModifier = (m & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        if (!withModifier && e.Key is not (>= Key.F1 and <= Key.F24 or Key.Snapshot)) return null;
        if (m == KeyModifiers.Control && (e.Key == Key.F || EditingKeys.Contains(e.Key))) return null;
        if (m == (KeyModifiers.Control | KeyModifiers.Shift) && EditingKeys.Contains(e.Key)) return null;
        return string.Join("+", Parts(m, e.Key.ToString()));
    }

    private static KeyModifiers ModifierOf(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
        Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
        Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
        Key.LWin or Key.RWin or Key.System => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };

    private static List<string> Parts(KeyModifiers m, string? key)
    {
        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        if (key is not null) parts.Add(key);
        return parts;
    }

    /// <summary>"Ctrl + Alt + ..." while only modifiers are held, "Ctrl + Alt + Up" once the main key is down; empty when nothing is held.</summary>
    private static string Preview(KeyModifiers m, string? key)
    {
        var parts = Parts(m, key);
        if (parts.Count == 0) return string.Empty;
        if (key is null) parts.Add(Ellipsis);
        return string.Join(" + ", parts);
    }
}
