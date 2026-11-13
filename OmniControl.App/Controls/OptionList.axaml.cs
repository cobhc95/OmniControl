using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Controls;

/// <summary>
/// Wiring only for <see cref="OptionListModel"/>: Up/Down move the highlight and wrap, the pointer moves it to the row under it (one lit
/// row, as in a menu), Enter or Space chooses, Esc asks the host to close (<see cref="CloseRequested"/>); a click chooses. Nothing here
/// knows what the options are.
/// </summary>
public partial class OptionList : UserControl
{
    public OptionList()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Esc was pressed: the host (a window) closes.</summary>
    public event Action? CloseRequested;

    private OptionListModel? Model => DataContext as OptionListModel;

    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OptionEntry entry) Model?.Choose(entry);
    }

    private void OnEntryPointerEntered(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is OptionEntry entry) Model?.HighlightEntry(entry);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Up or Key.Down:
                model.Move(Services.Wrap.Direction(e.Key));
                break;
            case Key.Enter or Key.Space:
                model.ChooseHighlighted();
                break;
            case Key.Escape:
                CloseRequested?.Invoke();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
