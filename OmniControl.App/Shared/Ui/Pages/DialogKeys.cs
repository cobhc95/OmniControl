using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Pages;

/// Esc closes every window that carries <c>Classes="dialog"</c>, registered once at start-up like <see cref="ValueWheel"/>. It listens as
/// the key bubbles up, so a drop-down, a text box or the picker that used Esc first keeps it and the window stays.
public static class DialogKeys
{
    public const string DialogClass = "dialog";

    public static void Register() =>
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Bubble);

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || !window.Classes.Contains(DialogClass)) return;
        window.Close();
        e.Handled = true;
    }
}
