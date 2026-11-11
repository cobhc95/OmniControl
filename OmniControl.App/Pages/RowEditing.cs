using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages;

/// <summary>
/// Keyboard model for a ListBox of cards, each holding editable parts (hotkey box, device picker, sliders...):
/// Up/Down move between cards, Left/Right move between the parts of the selected card,
/// Enter works on the highlighted part (start listening, open the list, adjust the slider), Esc backs out of that.
/// </summary>
public static class RowEditing
{
    private static Slider? _activeSlider;

    public static void Attach(ListBox list) =>
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>True while a part is being edited (listening, adjusting a slider, a list open), so Esc belongs to it.</summary>
    public static bool IsEditing(object? focused) =>
        HotkeyCapture.IsListening(focused)
        || (focused is Slider s && s == _activeSlider)
        || focused is ComboBox { IsDropDownOpen: true };

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
        var to = parts.IndexOf(src) + (e.Key == Key.Down ? 1 : -1);
        if (parts.IndexOf(src) < 0 || to < 0 || to >= parts.Count) return false; // at an end: move to the next card
        parts[to].Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Where focus goes when a card is entered: its first part (the hotkey box), or the card itself.</summary>
    public static InputElement CardTarget(ListBoxItem card) => Parts(card).FirstOrDefault() ?? card;

    private static bool IsPart(Control c) =>
        c is Slider or ComboBox or ToggleSwitch
        || c is Border { Tag: "gallery" }
        || c is Button { Tag: "scope" }
        || HotkeyCapture.IsHotkeyBox(c);

    private static List<Control> Parts(ListBoxItem card) =>
        card.GetVisualDescendants().OfType<Control>().Where(c => IsPart(c) && c.IsEffectivelyVisible).ToList();

    /// <summary>Arrow-key step: 5 on a 0-100 slider, 1 on a short range such as an EQ band.</summary>
    private static double StepFor(Slider s) => s.Maximum - s.Minimum > 50 ? 5 : 1;

    private static void Activate(Slider slider)
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

    /// <summary>For pages that host several card lists and route keys themselves.</summary>
    public static void Handle(ListBox list, KeyEventArgs e) => OnKeyDown(list, e);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || e.Source is not Control src) return;
        if (src is ComboBoxItem || src.FindAncestorOfType<ComboBoxItem>() is not null) return; // an open drop-down handles itself

        var card = src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>();
        if (card is null) return;

        // 1. Hotkey box: keys are the new binding while listening.
        if (src is TextBox { IsReadOnly: true, DataContext: HotkeyAction hotkey } && HotkeyCapture.TryHandle(e, hotkey))
        {
            e.Handled = true;
            return;
        }

        // 2. A slider being adjusted: Left/Right change it, Enter/Esc finish.
        if (src is Slider active && active == _activeSlider)
        {
            var step = e.KeyModifiers == KeyModifiers.Shift ? 1 : StepFor(active);
            switch (e.Key)
            {
                case Key.Left or Key.Right:
                    active.Value = Math.Clamp(active.Value + (e.Key == Key.Right ? step : -step), active.Minimum, active.Maximum);
                    break;
                case Key.Enter or Key.Escape: Deactivate(); break;
                case Key.Up or Key.Down: break; // stay on the slider
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (src is ComboBox { IsDropDownOpen: true }) return;

        if (TryNavigateInside(card, src, e)) { e.Handled = true; return; }

        var parts = Parts(card);

        switch (e.Key)
        {
            // A card whose only part is a slider needs no Enter: Left/Right change it directly.
            case Key.Left or Key.Right when src is Slider only && parts.Count == 1:
                var stepSize = e.KeyModifiers == KeyModifiers.Shift ? 1 : StepFor(only);
                only.Value = Math.Clamp(only.Value + (e.Key == Key.Right ? stepSize : -stepSize), only.Minimum, only.Maximum);
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

            case Key.Enter when src is Slider slider && parts.Count > 1:
                Activate(slider);
                e.Handled = true;
                break;

            case Key.Enter when src == card && parts.Count > 0:
                parts[0].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;

            // Left/Right: previous or next part of this card.
            case Key.Left or Key.Right when parts.Count > 0 && src != card:
                var i = parts.IndexOf(src);
                if (i >= 0) parts[Math.Clamp(i + (e.Key == Key.Right ? 1 : -1), 0, parts.Count - 1)].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;

            // Up/Down: previous or next card, landing on its first part.
            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.None:
                var count = list.ItemCount;
                var next = count == 0 ? -1 : (list.IndexFromContainer(card) + (e.Key == Key.Down ? 1 : -1) + count) % count; // wraps round at either end
                if (next >= 0 && next < list.ItemCount && list.ContainerFromIndex(next) is ListBoxItem target)
                {
                    list.SelectedIndex = next;
                    CardTarget(target).Focus(NavigationMethod.Directional);
                }
                e.Handled = true;
                break;
        }
    }
}
