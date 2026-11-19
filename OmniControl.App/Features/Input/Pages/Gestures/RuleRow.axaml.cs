using Avalonia.Input;

namespace OmniControl.App.Features.Input.Pages.Gestures;

/// One rule's line. Its Edit pill opens the rule editor; focus comes back to the pill afterwards.
public partial class RuleRow : UserControl
{
    public RuleRow() => InitializeComponent();

    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RuleItem item || TopLevel.GetTopLevel(this) is not Window owner) return;
        await RuleEditorWindow.ShowAsync(owner, item.Editor());
        if (Edit.IsEffectivelyVisible) Edit.Focus(NavigationMethod.Directional);
    }
}
