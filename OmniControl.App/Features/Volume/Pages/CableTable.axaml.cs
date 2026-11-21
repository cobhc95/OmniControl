using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Pages;

/// The Virtual cables group: the dashed chip opens the pop-down that marks a pair by hand (the page fills its drop-downs as it opens);
/// Add or Esc closes it and the keyboard goes back to the chip.
public partial class CableTable : UserControl
{
    // The chip is built only while the group is open, so the one that opened the pop-down is kept to close it and get the keyboard back.
    private Control? _chip;

    public CableTable() => InitializeComponent();

    private void OnMarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control chip) return;
        _chip = chip;
        FlyoutBase.ShowAttachedFlyout(chip);
    }

    private void OnMarkOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } || DataContext is not CableGroup group) return;
        panel.DataContext = group.Mark;
        group.Mark.Open();
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => CloseMark();

    private void OnMarkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseMark();
        e.Handled = true;
    }

    private void CloseMark()
    {
        if (_chip is not { } chip) return;
        FlyoutBase.GetAttachedFlyout(chip)?.Hide();
        chip.Focus(NavigationMethod.Directional);
    }
}
