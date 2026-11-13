using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Picker;

namespace OmniControl.App.Pages.Gestures;

/// <summary>
/// Wiring only for <see cref="RuleEditorViewModel"/>: blocks open the master picker on their slot, the cross and Delete remove a
/// block, Left/Right walk the sentence (wrapping), Esc closes. Focus stays at the same place in the sentence after it is rebuilt.
/// </summary>
public partial class RuleEditorWindow : Window
{
    public RuleEditorWindow()
    {
        InitializeComponent();
        RowEditing.Attach(SettingsList);
        AddHandler(KeyDownEvent, OnSentenceKey, RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return; // a drop-down or the name box used it first
            Close();
            e.Handled = true;
        };
        Opened += (_, _) => FocusSentence(0);
    }

    private RuleEditorViewModel? Model => DataContext as RuleEditorViewModel;

    /// <summary>Opens the editor as a dialog over <paramref name="owner"/> and waits until it closes.</summary>
    public static async Task ShowAsync(Window owner, RuleEditorViewModel model)
    {
        await new RuleEditorWindow { DataContext = model }.ShowDialog(owner);
        model.Closed();
    }

    /// <summary>The sentence's buttons in reading order (the crosses inside blocks are not stops).</summary>
    private List<Button> SentenceButtons() =>
        SentenceHost.GetVisualDescendants().OfType<Button>().Where(b => !b.Classes.Contains("blockx") && b.IsEffectivelyVisible).ToList();

    /// <summary>Puts the keyboard on the sentence's button at <paramref name="index"/> (or the last one) once the sentence is drawn again.</summary>
    private void FocusSentence(int index) => Dispatcher.UIThread.Post(() =>
    {
        var buttons = SentenceButtons();
        if (buttons.Count > 0) buttons[Math.Clamp(index, 0, buttons.Count - 1)].Focus(NavigationMethod.Directional);
    }, DispatcherPriority.Loaded);

    private int SentenceIndexOf(object? control) => control is Button button ? SentenceButtons().IndexOf(button) : -1;

    private void OnSentenceKey(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model || e.KeyModifiers != KeyModifiers.None) return;
        var focused = FocusManager?.GetFocusedElement();
        var at = SentenceIndexOf(focused);
        if (at < 0) return;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                var buttons = SentenceButtons();
                buttons[Services.Wrap.Index(at + (e.Key == Key.Right ? 1 : -1), buttons.Count)].Focus(NavigationMethod.Directional);
                break;
            case Key.Delete or Key.Back when (focused as Control)?.DataContext is SentenceBlock block:
                model.Remove(block.Slot);
                FocusSentence(at);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private async Task PickAsync(RuleSlot slot, int focusAfter)
    {
        if (Model is not { } model) return;
        var (title, picker) = model.PickerFor(slot);
        void Live(PickResult pick) => model.TakeLive(slot, pick);
        picker.Live += Live;
        var chosen = await PickerWindow.ChooseAsync(this, title, picker);
        picker.Live -= Live;
        if (chosen is not null) await model.TakeAsync(slot, chosen);
        FocusSentence(focusAfter);
    }

    private async void OnBlockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SentenceBlock block }) await PickAsync(block.Slot, SentenceIndexOf(sender));
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SentenceAdd add }) await PickAsync(add.Slot, SentenceIndexOf(sender));
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true; // the block around the cross must not open the picker
        if (Model is { } model && sender is Control { DataContext: SentenceBlock block }) model.Remove(block.Slot);
    }

    private void OnJoinClick(object? sender, RoutedEventArgs e)
    {
        var at = SentenceIndexOf(sender);
        Model?.FlipJoin();
        FocusSentence(at);
    }

    private void OnTestClick(object? sender, RoutedEventArgs e) => Model?.Test();

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && await model.DeleteAsync()) Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(model.Details);
        }
        catch (Exception ex) when (ex is TimeoutException or ExternalException)
        {
            // Another program holds the clipboard: the details are still in the rules log, whose path the status line names.
        }
    }
}
