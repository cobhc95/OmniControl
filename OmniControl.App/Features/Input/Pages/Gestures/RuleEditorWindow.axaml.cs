using Avalonia.Input;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Input.Pages.Gestures;

/// Wiring only for <see cref="RuleEditorViewModel"/>: blocks open the master picker on their slot, the cross and Delete remove a
/// block, Left/Right walk the sentence (wrapping), Esc closes. Focus stays at the same place in the sentence after it is rebuilt.
public partial class RuleEditorWindow : Window
{
    public RuleEditorWindow()
    {
        InitializeComponent();
        RowEditing.Attach(SettingsList);
        AddHandler(KeyDownEvent, OnSentenceKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => FocusSentence(0);
    }

    private RuleEditorViewModel? Model => DataContext as RuleEditorViewModel;

    /// Opens the editor as a dialog over <paramref name="owner"/> and waits until it closes.
    public static async Task ShowAsync(Window owner, RuleEditorViewModel model)
    {
        await new RuleEditorWindow { DataContext = model }.ShowDialog(owner);
        model.Closed();
    }

    private List<Button> SentenceButtons() => SentenceFocus.ButtonsOf(SentenceHost);

    private void FocusSentence(int index) => SentenceFocus.FocusLater(SentenceHost, index);

    private int SentenceIndexOf(object? control) => SentenceFocus.IndexOf(SentenceHost, control);

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
                buttons[Shared.Services.Wrap.Index(at + (e.Key == Key.Right ? 1 : -1), buttons.Count)].Focus(NavigationMethod.Directional);
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

    private void OnBlockClick(object? sender, RoutedEventArgs e) => Guard.Fire("Gesture rules: pick a rule part", async () =>
    {
        if (sender is Control { DataContext: SentenceBlock block }) await PickAsync(block.Slot, SentenceIndexOf(sender));
    });

    private void OnAddClick(object? sender, RoutedEventArgs e) => Guard.Fire("Gesture rules: add a rule part", async () =>
    {
        if (sender is Control { DataContext: SentenceAdd add }) await PickAsync(add.Slot, SentenceIndexOf(sender));
    });

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

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Guard.Fire("Gesture rules: delete a rule", async () =>
    {
        if (Model is { } model && await model.DeleteAsync()) Close();
    });

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, Model?.Details);
}
