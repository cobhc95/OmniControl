using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// App-wide keyboard model of every drop-down (ComboBox), registered once at start-up like <see cref="ValueWheel"/>.
/// Closed: Alt+Down, F4, Space or Enter open it. Open: Up/Down move through the entries (wrapping), Home/End jump to the first or last,
/// Enter or Space picks the highlighted one, Esc closes without a change, typing letters jumps to the next entry that starts with them.
/// Closing returns the keyboard to the drop-down so the card walk carries on. It runs at the top of the window (and of the pop-down's own
/// root), ahead of every page and window key handler, so no card router or "Esc closes the window" handler sees these keys first.
/// </summary>
public static class ComboKeys
{
    public enum Act { None, Open, Close, Highlight, Pick }

    /// <summary>What a key does to a drop-down; <see cref="Index"/> is the entry to highlight for <see cref="Act.Highlight"/>.</summary>
    public readonly record struct Result(Act Act, int Index = -1);

    private static string _typed = string.Empty;
    private static DateTime _typedAt;

    public static void Register() =>
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>The key rules, free of any control. <paramref name="current"/> is the highlighted entry (the selected one when none is).</summary>
    public static Result Decide(Key key, KeyModifiers mods, bool open, int count, int current)
    {
        var alt = mods == KeyModifiers.Alt;
        if (count <= 0) return key == Key.Escape && open ? new(Act.Close) : default;
        if (!open)
            return key is Key.F4 && mods == KeyModifiers.None || key is Key.Down && alt
                || key is Key.Enter or Key.Space && mods == KeyModifiers.None
                ? new(Act.Open) : default;

        if (key is Key.F4 && mods == KeyModifiers.None || key is Key.Up or Key.Down && alt || key == Key.Escape && mods == KeyModifiers.None)
            return new(Act.Close);
        if (mods != KeyModifiers.None) return default;
        return key switch
        {
            Key.Up or Key.Down => new(Act.Highlight, Wrap.Index((current < 0 ? (key == Key.Down ? -1 : 0) : current) + Wrap.Direction(key), count)),
            Key.Home => new(Act.Highlight, 0),
            Key.End => new(Act.Highlight, count - 1),
            Key.Enter or Key.Space => new(Act.Pick, current),
            _ => default,
        };
    }

    /// <summary>The first entry after <paramref name="from"/> (wrapping) whose label starts with <paramref name="typed"/>; -1 for none.</summary>
    public static int FindByPrefix(IReadOnlyList<string> labels, string typed, int from)
    {
        for (var n = 1; n <= labels.Count; n++)
        {
            var i = Wrap.Index(from + n, labels.Count);
            if (labels[i].StartsWith(typed, StringComparison.CurrentCultureIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>The next or previous entry of a closed drop-down, stopping at the ends (the wheel and Left/Right).</summary>
    public static void Step(ComboBox combo, int by)
    {
        if (combo.IsDropDownOpen || combo.ItemCount == 0) return;
        combo.SelectedIndex = Math.Clamp(Math.Max(0, combo.SelectedIndex) + by, 0, combo.ItemCount - 1);
    }

    private static void OnKeyDown(TopLevel _, KeyEventArgs e)
    {
        if (e.Handled || e.Source is not Control src || FindCombo(src) is not { } combo) return;
        var open = combo.IsDropDownOpen;
        if (!open && src != combo) return;

        var focusedItem = src as ComboBoxItem ?? src.FindAncestorOfType<ComboBoxItem>();
        var current = focusedItem is null ? combo.SelectedIndex : combo.IndexFromContainer(focusedItem);
        var result = Decide(e.Key, e.KeyModifiers, open, combo.ItemCount, current);

        if (result.Act == Act.None && open && TypeAhead(combo, e, current)) { e.Handled = true; return; }
        switch (result.Act)
        {
            case Act.Open: combo.IsDropDownOpen = true; break;
            case Act.Close: Close(combo); break;
            case Act.Highlight: combo.ContainerFromIndex(result.Index)?.Focus(NavigationMethod.Directional); break;
            case Act.Pick:
                if (result.Index >= 0) combo.SelectedIndex = result.Index;
                Close(combo);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private static ComboBox? FindCombo(Control src)
    {
        if ((src as ComboBoxItem ?? src.FindAncestorOfType<ComboBoxItem>()) is { } item) return ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
        return src as ComboBox ?? src.FindAncestorOfType<ComboBox>();
    }

    private static void Close(ComboBox combo)
    {
        combo.IsDropDownOpen = false;
        combo.Focus(NavigationMethod.Directional);
    }

    private static bool TypeAhead(ComboBox combo, KeyEventArgs e, int current)
    {
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift) || e.KeySymbol is not { Length: > 0 } symbol || char.IsControl(symbol[0])) return false;
        var now = DateTime.UtcNow;
        _typed = now - _typedAt > Timing.TypeAheadReset ? symbol : _typed + symbol;
        _typedAt = now;

        var labels = Enumerable.Range(0, combo.ItemCount).Select(i => LabelOf(combo, i)).ToList();
        // One letter goes on to the next entry that starts with it (so repeating it cycles); a longer word may stay on the current entry.
        var found = FindByPrefix(labels, _typed, _typed.Length == 1 ? current : current - 1);
        if (found >= 0) combo.ContainerFromIndex(found)?.Focus(NavigationMethod.Directional);
        return true;
    }

    private static string LabelOf(ComboBox combo, int index) =>
        combo.ContainerFromIndex(index)?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text
        ?? combo.Items[index]?.ToString() ?? string.Empty;
}
