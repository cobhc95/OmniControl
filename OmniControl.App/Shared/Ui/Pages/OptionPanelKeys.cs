using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Keys inside a pop-down of option cards (<c>Button.optioncard</c>; the Method menu): Up/Down move between the cards and wrap,
/// Right/Left walk a card's own buttons (Install, Choose devices...) and back to the card, Enter picks (the button's own click), Esc
/// closes. A picked card closes the pop-down; a card's own button leaves it open. One shared piece for every such pop-down: made
/// when the pop-down opens, disposed when it closes.
public sealed class OptionPanelKeys : IDisposable
{
    private const string CardClass = "optioncard";

    private readonly Control _panel;
    private readonly Action _close;

    /// Wires <paramref name="panel"/> (the pop-down's content) and puts the keyboard on the selected card, else the first.
    public OptionPanelKeys(Control panel, Action close)
    {
        _panel = panel;
        _close = close;
        var cards = Cards(panel);
        (cards.FirstOrDefault(c => c.Classes.Contains("selected")) ?? cards.FirstOrDefault())?.Focus(NavigationMethod.Directional);
        panel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        panel.AddHandler(Button.ClickEvent, OnClick);
    }

    public void Dispose()
    {
        _panel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _panel.RemoveHandler(Button.ClickEvent, OnClick);
    }

    private static bool IsCard(Button b) => b.Classes.Contains(CardClass);

    private static List<Button> Cards(Control panel) => panel.GetVisualDescendants().OfType<Button>().Where(IsCard).ToList();

    /// The card that holds the focused element (the card itself or one of its buttons).
    private static Button? CardOf(Control? focused) =>
        focused is null ? null : new Visual[] { focused }.Concat(focused.GetVisualAncestors()).OfType<Button>().FirstOrDefault(IsCard);

    /// The card's own buttons, in order.
    private static List<Button> ActionsOf(Button card) =>
        card.GetVisualDescendants().OfType<Button>().Where(b => b != card && b.IsEffectivelyVisible).ToList();

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button b && IsCard(b)) _close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var panel = _panel;
        if (e.KeyModifiers != KeyModifiers.None) return;
        var focused = TopLevel.GetTopLevel(panel)?.FocusManager?.GetFocusedElement() as Control;
        var card = CardOf(focused);

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                var cards = Cards(panel);
                var at = card is null ? (e.Key == Key.Up ? 0 : -1) : cards.IndexOf(card); // nothing focused: Down lands on the first, Up on the last
                if (cards.Count > 0) cards[Services.Wrap.Index(at + Services.Wrap.Direction(e.Key), cards.Count)].Focus(NavigationMethod.Directional);
                break;

            case Key.Right when card is not null && ActionsOf(card) is { Count: > 0 } next && next.IndexOf(focused as Button ?? card) < next.Count - 1:
                next[next.IndexOf(focused as Button ?? card) + 1].Focus(NavigationMethod.Directional);
                break;

            case Key.Left when card is not null && focused != card:
                var actions = ActionsOf(card);
                var index = actions.IndexOf(focused as Button ?? card);
                (index > 0 ? actions[index - 1] : card).Focus(NavigationMethod.Directional);
                break;

            case Key.Escape:
                _close();
                break;

            default:
                return;
        }
        e.Handled = true;
    }
}
