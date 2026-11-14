using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Wiring only: the dashed chip, the one-device choice or a click on the cell's empty part opens the cell's menu (attached to the whole
/// cell, so it opens under it); the row fills it as it opens. A choice or Esc closes it; the keyboard goes back to the button it was
/// opened from when it was opened from the keyboard. A combined microphone's member chip opens its level pop-down, or, pressed on its
/// x, takes the member away.
/// </summary>
public partial class WiringRowView : UserControl
{
    /// <summary>The Tag of a member chip's x (its hit area).</summary>
    private const string RemoveTag = "chipremove";

    private Flyout? _menu;
    private Control? _opener;
    private bool _openedByKeyboard;
    private bool _pressedOnRemove;

    public WiringRowView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnOpenMenuClick(object? sender, RoutedEventArgs e) => OpenMenu(sender as Control);

    private void OnCellTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WiringRow) return;
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return; // a chip or the button acts on its own click
        OpenMenu(Cell.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Tag is "scope"));
    }

    private void OpenMenu(Control? opener)
    {
        _opener = opener;
        _openedByKeyboard = opener?.Classes.Contains(":focus-visible") == true; // the keyboard's focus look, not a click's focus
        FlyoutBase.ShowAttachedFlyout(Cell);
    }

    private void OnMenuOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } flyout || DataContext is not WiringRow row) return;
        _menu = flyout;
        panel.DataContext = row;
        row.OpenMenu();
    }

    private void OnMenuChoiceClick(object? sender, RoutedEventArgs e) => CloseMenu(_openedByKeyboard);

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseMenu(returnFocus: true);
        e.Handled = true;
    }

    /// <summary>Esc while the keyboard is still on the row (the menu does not take it).</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _menu is not { IsOpen: true }) return;
        CloseMenu(returnFocus: true);
        e.Handled = true;
    }

    private void CloseMenu(bool returnFocus)
    {
        _menu?.Hide();
        if (returnFocus) _opener?.Focus(NavigationMethod.Directional);
    }

    // ---- a combined microphone's member chip ----

    /// <summary>Remembers whether a press began on a member chip's x (the chip's click then takes the member away).</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressedOnRemove = (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true) is { Tag: RemoveTag };

    private void OnMemberClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: WiringMember member, Parent: Control holder }) return;
        if (_pressedOnRemove) member.RemoveCommand.Execute(null);
        else FlyoutBase.ShowAttachedFlyout(holder);
        _pressedOnRemove = false;
    }

    private void OnMemberMenuOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control view, Target.DataContext: WiringMember member }) return;
        view.DataContext = member.Menu;
        member.Menu.Refresh();
    }
}
