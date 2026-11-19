using System.Collections.Specialized;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// Layout and focus only: the hotkey part of the settings pane (<see cref="ActionHotkeys"/>); its keys are routed by <see cref="PickerMenu"/>.
/// A box that starts listening on its own ("+ add key") is given the keys, so what is pressed next reaches it; so is the box of a key just set, after the boxes are rebuilt.
public partial class ActionHotkeysView : UserControl
{
    private ActionHotkeys? _shown;

    public ActionHotkeysView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => Follow(null);
    }

    private async void OnChangeIconClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_shown is { } pane) await pane.ChangeIconAsync(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => Follow(DataContext as ActionHotkeys);

    private void Follow(ActionHotkeys? pane)
    {
        if (_shown is not null) _shown.Slots.CollectionChanged -= OnSlotsChanged;
        _shown = pane;
        if (pane is not null) pane.Slots.CollectionChanged += OnSlotsChanged;
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems?[0] is not HotkeyKeySlot slot || _shown is not { } pane) return;
        Dispatcher.UIThread.Post(() =>
        {
            var given = slot.Key.Length > 0 && slot.Key == pane.KeyToFocus;
            if (!slot.IsListening && !given) return;
            if (given) pane.KeyToFocus = null;
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.DataContext == slot)?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);
    }
}
