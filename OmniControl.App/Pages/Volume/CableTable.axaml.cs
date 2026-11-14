using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Virtual cables group: the dashed chip opens the pop-down that marks a pair by hand (the page fills its drop-downs as it opens);
/// Add or Esc closes it and the keyboard goes back to the chip.
/// </summary>
public partial class CableTable : UserControl
{
    public CableTable() => InitializeComponent();

    private void OnMarkClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(MarkChip);

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
        FlyoutBase.GetAttachedFlyout(MarkChip)?.Hide();
        MarkChip.Focus(NavigationMethod.Directional);
    }
}
