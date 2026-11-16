using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Picker;

namespace OmniControl.App.Pages.Automation;

/// <summary>
/// Wiring only for <see cref="AutomationItem"/>: a block opens the master picker on its slot (the hotkey block opens its capture
/// pop-down instead, and <see cref="HotkeyCapture"/> turns the keys into the rule's shortcut), the cross and Delete remove a block,
/// and the keyboard goes back to the same place in the sentence once it is rebuilt.
/// </summary>
public partial class AutomationRuleRow : UserControl
{
    private AutomationItem? _watched;

    public AutomationRuleRow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    private AutomationItem? Item => DataContext as AutomationItem;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null) _watched.PropertyChanged -= OnItemChanged;
        _watched = Item;
        if (_watched is not null) _watched.PropertyChanged += OnItemChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CapturePopup.IsOpen = false;
    }

    // The pop-down closes the moment the rule stops listening; it opens when the rebuilt block is drawn (OnHotkeyBlockLoaded).
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutomationItem.IsCapturing) && Item is { IsCapturing: false }) CapturePopup.IsOpen = false;
    }

    private static RuleSlot? SlotOf(object? control) => (control as Control)?.DataContext switch
    {
        SentenceBlock block => block.Slot,
        SentenceHotkey hotkey => hotkey.Slot,
        _ => null,
    };

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Item is not { } item) return;
        if (item.OwnAction is { IsListening: true } action)
        {
            if (HotkeyCapture.TryHandle(e, action)) e.Handled = true;
            return;
        }
        if (e.Key is Key.Delete or Key.Back && e.KeyModifiers == KeyModifiers.None
            && SlotOf(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()) is { } slot)
        {
            var at = SentenceFocus.IndexOf(SentenceHost, TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement());
            item.Remove(slot);
            SentenceFocus.FocusLater(SentenceHost, at);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (Item?.OwnAction is { IsListening: true } action && HotkeyCapture.TryHandleUp(e, action)) e.Handled = true;
    }

    private async Task PickAsync(RuleSlot slot, int focusAfter)
    {
        if (Item is not { } item || TopLevel.GetTopLevel(this) is not Window owner) return;
        var (title, picker) = item.PickerFor(slot);
        void Live(PickResult pick) => item.TakeLive(slot, pick);
        picker.Live += Live;
        var chosen = await PickerWindow.ChooseAsync(owner, title, picker);
        picker.Live -= Live;
        if (chosen is not null) await item.TakeAsync(slot, chosen);
        SentenceFocus.FocusLater(SentenceHost, focusAfter);
    }

    private async void OnBlockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SentenceBlock block }) return;
        await PickAsync(block.Slot, SentenceFocus.IndexOf(SentenceHost, sender));
    }

    /// <summary>The "I press" block: starts listening (its pop-down opens when the block is drawn again).</summary>
    private void OnHotkeyClick(object? sender, RoutedEventArgs e)
    {
        if (Item is not { } item) return;
        item.ListenForHotkey();
        SentenceFocus.FocusLater(SentenceHost, SentenceFocus.IndexOf(SentenceHost, sender));
    }

    /// <summary>The block is drawn: the pop-down hangs under it, open while the rule listens.</summary>
    private void OnHotkeyBlockLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SentenceHotkey hotkey } block) return;
        CapturePopup.PlacementTarget = block;
        CapturePopup.IsOpen = hotkey.Capturing;
    }

    private void OnCaptureClearClick(object? sender, RoutedEventArgs e) => Item?.ClearHotkey();

    private void OnCaptureCancelClick(object? sender, RoutedEventArgs e) => Item?.StopListening();

    // Leaving the row (Tab, clicking elsewhere) stops waiting for keys. The sentence is rebuilt after every change and the keyboard put
    // back a moment later, so the question is asked once that has happened.
    private void OnLostFocus(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (Item is { OwnAction.IsListening: true } item && !IsKeyboardFocusWithin) item.StopListening();
    }, DispatcherPriority.Background);

    /// <summary>A choice in a join word's pop-down: closes it and says it; the sentence is rebuilt, so the keyboard goes back to the same word.</summary>
    private void OnLinkOptionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SentenceLinkOption option } row || row.FindLogicalAncestorOfType<Button>() is not { } join) return;
        var at = SentenceFocus.IndexOf(SentenceHost, join);
        join.Flyout?.Hide();
        option.Choose();
        SentenceFocus.FocusLater(SentenceHost, at);
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SentenceAdd add }) await PickAsync(add.Slot, SentenceFocus.IndexOf(SentenceHost, sender));
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true; // the block around the cross must not open the picker
        if (Item is { } item && SlotOf(sender) is { } slot) item.Remove(slot);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Item is { } item) await item.DeleteAsync();
    }
}
