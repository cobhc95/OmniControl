using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OmniControl.App.Controls;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages;

/// <summary>
/// Keyboard model for a ListBox of cards, each holding editable parts (hotkey box, device picker, sliders...):
/// Up/Down move between cards, Left/Right move between the parts of the selected card,
/// Enter works on the highlighted part (start listening, open the list, adjust the slider), Esc backs out of that.
/// A vertical slider (a graphic equaliser's band) keeps Up/Down for its value; Esc on it gives the keyboard back to its card.
/// A knob is a part like a slider: Enter starts adjusting it (its own keys, <see cref="KnobMath.KeyStep"/>, then turn it), Enter or Esc
/// finish; Home resets it; Ctrl+Enter types its value.
/// </summary>
public static class RowEditing
{
    /// <summary>The slider or knob being adjusted (Enter on it), whose keys change it until Enter or Esc.</summary>
    private static RangeBase? _activeSlider;

    public static void Attach(ListBox list) =>
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>
    /// True while a part is being edited (listening, adjusting a slider, typing a value, a list open, on a vertical slider, whose
    /// Up/Down change it), so Esc and Up/Down belong to it.
    /// </summary>
    public static bool IsEditing(object? focused) =>
        HotkeyCapture.IsListening(focused)
        || (focused is RangeBase r && r == _activeSlider)
        || (focused is Slider s && IsVertical(s))
        || NumberText.IsTypingIn(focused)
        || focused is ComboBox { IsDropDownOpen: true } or ComboBoxItem;

    /// <summary>
    /// A card whose view-model is <see cref="ISideNavigable"/> (the equaliser) uses Left/Right for its own sideways navigation
    /// (bands) and Up/Down to walk its parts one by one, leaving the card at either end.
    /// Returns true when the key was used. Pages that route keys themselves call this first.
    /// </summary>
    public static bool TryNavigateInside(ListBoxItem card, Control src, KeyEventArgs e)
    {
        if (card.DataContext is not ISideNavigable side || src == card || e.KeyModifiers != KeyModifiers.None || IsEditing(src)) return false;
        if (e.Key is Key.Left or Key.Right)
        {
            side.Step(e.Key == Key.Right ? 1 : -1);
            return true;
        }
        if (e.Key is not (Key.Up or Key.Down)) return false;
        var parts = Parts(card);
        var to = parts.IndexOf(src) + Services.Wrap.Direction(e.Key);
        if (parts.IndexOf(src) < 0 || to < 0 || to >= parts.Count) return false; // at an end: move to the next card
        parts[to].Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Where focus goes when a card is entered: its first part (the hotkey box), or the card itself.</summary>
    public static InputElement CardTarget(ListBoxItem card) => Parts(card).FirstOrDefault() ?? card;

    private static bool IsPart(Control c) =>
        c is Slider or Knob or ComboBox or ToggleSwitch
        || c is Border { Tag: "gallery" }
        || c is Button { Tag: "scope" }
        || HotkeyCapture.IsHotkeyBox(c)
        || c is TextBox { IsReadOnly: false };

    // A disabled part cannot take focus, so it is not a stop (a card with no stops is focused as a whole); nor is a part a folding
    // panel left out (given no room: a chip folded into "+N more", a toolbar control of a FoldingRow).
    private static List<Control> Parts(ListBoxItem card) =>
        card.GetVisualDescendants().OfType<Control>()
            .Where(c => IsPart(c) && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds.Width > 0).ToList();

    private static bool IsVertical(Slider s) => s.Orientation == Orientation.Vertical;

    /// <summary>
    /// Keys of a vertical slider: Up/Down one step (its SmallChange), with Shift or as PageUp/PageDown a large step (LargeChange),
    /// with Ctrl one tick; Home or 0 = its reset value (<see cref="SliderReset"/>); Esc = back to the card, from which Up/Down move
    /// between cards again. False for any other key (Left/Right walk the card's parts as usual).
    /// </summary>
    private static bool StepVertical(Slider s, ListBoxItem card, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        double? by = e.Key switch
        {
            Key.Up or Key.Down when mods == KeyModifiers.None => s.SmallChange,
            Key.Up or Key.Down when mods == KeyModifiers.Shift => s.LargeChange,
            Key.Up or Key.Down when mods == KeyModifiers.Control => s.TickFrequency,
            Key.PageUp or Key.PageDown when mods == KeyModifiers.None => s.LargeChange,
            _ => null,
        };
        if (by is { } amount)
        {
            s.Value = Math.Clamp(s.Value + (e.Key is Key.Up or Key.PageUp ? amount : -amount), s.Minimum, s.Maximum);
            return true;
        }
        if (mods != KeyModifiers.None) return false;
        if (e.Key is Key.Home or Key.D0 or Key.NumPad0 && SliderReset.GetValue(s) is { } reset)
        {
            s.Value = reset;
            return true;
        }
        if (e.Key != Key.Escape) return false;
        card.Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Left/Right on a slider: 5 on a 0-100 slider, 1 on a short range (50 or less), 1 with Shift held.</summary>
    private static void StepSlider(Slider s, KeyEventArgs e)
    {
        var step = e.KeyModifiers == KeyModifiers.Shift || s.Maximum - s.Minimum <= 50 ? 1 : 5;
        s.Value = Math.Clamp(s.Value + (e.Key == Key.Right ? step : -step), s.Minimum, s.Maximum);
    }

    private static void Activate(RangeBase slider)
    {
        _activeSlider = slider;
        slider.Classes.Set("active", true);
        slider.LostFocus += OnSliderLostFocus;
    }

    private static void Deactivate()
    {
        if (_activeSlider is null) return;
        _activeSlider.Classes.Set("active", false);
        _activeSlider.LostFocus -= OnSliderLostFocus;
        _activeSlider = null;
    }

    private static void OnSliderLostFocus(object? sender, RoutedEventArgs e) => Deactivate();

    /// <summary>
    /// Left/Right past the first or last part of a half-width card goes to the neighbouring half-width card (right: its first part,
    /// left: its last), so a row of two cards can be walked sideways. Full-width cards stay put.
    /// </summary>
    private static void HopToNeighbour(ListBox list, ListBoxItem card, int step)
    {
        if (card.DataContext is not IHalfWidth { Half: true }) return;
        var to = list.IndexFromContainer(card) + step;
        if (to < 0 || to >= list.ItemCount || list.ContainerFromIndex(to) is not ListBoxItem neighbour || neighbour.DataContext is not IHalfWidth { Half: true }) return;
        list.SelectedIndex = to;
        (step > 0 ? CardTarget(neighbour) : Parts(neighbour).LastOrDefault() ?? neighbour).Focus(NavigationMethod.Directional);
    }

    /// <summary>For pages that host several card lists and route keys themselves.</summary>
    public static void Handle(ListBox list, KeyEventArgs e) => OnKeyDown(list, e);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || e.Source is not Control src) return;
        if (src is ComboBoxItem || src.FindAncestorOfType<ComboBoxItem>() is not null) return; // an open drop-down handles itself

        var card = src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>();
        if (card is null) return;

        // A text box keeps Left/Right/Home/End/Enter for typing; only Up/Down and Tab leave it.
        if (src is TextBox { IsReadOnly: false } && e.Key is not (Key.Up or Key.Down)) return;

        // 1. Hotkey box: keys are the new binding while listening.
        if (src is TextBox { IsReadOnly: true, DataContext: HotkeyTarget hotkey } && HotkeyCapture.TryHandle(e, hotkey))
        {
            e.Handled = true;
            return;
        }

        // 2. Ctrl+Enter on a slider types its value in (the same as a double-click on the value).
        if (src is Slider typed && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control && NumberText.TryBeginBeside(typed))
        {
            Deactivate();
            e.Handled = true;
            return;
        }

        // 3a. A knob being adjusted: Enter/Esc finish; its own keys turn it (they reach it unhandled).
        if (src is Knob turning && turning == _activeSlider)
        {
            if (e.Key is not (Key.Enter or Key.Escape) || e.KeyModifiers != KeyModifiers.None) return;
            Deactivate();
            e.Handled = true;
            return;
        }

        // 3b. A slider being adjusted: Left/Right change it, Enter/Esc finish.
        if (src is Slider active && active == _activeSlider)
        {
            switch (e.Key)
            {
                case Key.Left or Key.Right:
                    StepSlider(active, e);
                    break;
                case Key.Enter or Key.Escape: Deactivate(); break;
                case Key.Up or Key.Down: break; // stay on the slider
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (src is ComboBox { IsDropDownOpen: true }) return;

        // 4. A vertical slider: its value keys and Esc.
        if (src is Slider vertical && IsVertical(vertical) && StepVertical(vertical, card, e)) { e.Handled = true; return; }

        if (TryNavigateInside(card, src, e)) { e.Handled = true; return; }

        var parts = Parts(card);

        switch (e.Key)
        {
            // A card whose only part is a slider needs no Enter: Left/Right change it directly.
            case Key.Left or Key.Right when src is Slider only && parts.Count == 1:
                StepSlider(only, e);
                e.Handled = true;
                break;

            // A lone drop-down: Left/Right change the value as the wheel does.
            case Key.Left or Key.Right when src is ComboBox lone && parts.Count == 1:
                ComboKeys.Step(lone, e.Key == Key.Right ? 1 : -1);
                e.Handled = true;
                break;

            // A lone gallery (style, colours): Left/Right browse the options.
            case Key.Left or Key.Right when src is Border { Tag: "gallery", DataContext: GalleryItem gallery } && parts.Count == 1:
                gallery.Step(e.Key == Key.Right ? 1 : -1);
                e.Handled = true;
                break;

            case Key.Enter when src is Border { Tag: "gallery", DataContext: GalleryItem gallery2 }:
                gallery2.Step(1);
                e.Handled = true;
                break;

            // A lone on/off switch: Left = off, Right = on, Enter flips it.
            case Key.Left or Key.Right when src is ToggleSwitch sw && parts.Count == 1:
                sw.IsChecked = e.Key == Key.Right;
                e.Handled = true;
                break;

            case Key.Enter when src is ToggleSwitch sw2:
                sw2.IsChecked = sw2.IsChecked != true;
                e.Handled = true;
                break;

            case Key.Enter when src is Slider slider && !IsVertical(slider) && parts.Count > 1:
                Activate(slider);
                e.Handled = true;
                break;

            case Key.Enter when src is Knob knob && e.KeyModifiers == KeyModifiers.None:
                Activate(knob);
                e.Handled = true;
                break;

            // Home on a knob: its reset value (the same as a double-click).
            case Key.Home when src is Knob reset && SliderReset.GetValue(reset) is { } home:
                reset.Value = home;
                e.Handled = true;
                break;

            case Key.Enter when src == card && parts.Count > 0:
                parts[0].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;

            // Left/Right: previous or next part of this card.
            case Key.Left or Key.Right when parts.Count > 0 && src != card:
                var i = parts.IndexOf(src);
                var step = e.Key == Key.Right ? 1 : -1;
                if (i >= 0 && i + step >= 0 && i + step < parts.Count) parts[i + step].Focus(NavigationMethod.Directional);
                else if (i >= 0) HopToNeighbour(list, card, step);
                e.Handled = true;
                break;

            // Up/Down: previous or next card, landing on its first part.
            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.None:
                var next = list.ItemCount == 0 ? -1 : Services.Wrap.Index(list.IndexFromContainer(card) + Services.Wrap.Direction(e.Key), list.ItemCount);
                if (next >= 0 && list.ContainerFromIndex(next) is ListBoxItem target)
                {
                    list.SelectedIndex = next;
                    CardTarget(target).Focus(NavigationMethod.Directional);
                }
                e.Handled = true;
                break;
        }
    }
}
