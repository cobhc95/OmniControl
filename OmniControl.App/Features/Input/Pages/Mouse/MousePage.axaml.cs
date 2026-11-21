using Avalonia.Input;
using OmniControl.App.Features.Input.Pages.Gestures;

namespace OmniControl.App.Features.Input.Pages.Mouse;

public partial class MousePage : UserControl
{
    public MousePage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }

    /// "Add rule": the gesture groups make the rule, this opens its editor; focus comes back to the button.
    private void OnAddRule(object? sender, RoutedEventArgs e) => Guard.Fire("Mouse: add a gesture rule", async () =>
    {
        if (DataContext is not MousePageViewModel page || TopLevel.GetTopLevel(this) is not Window owner) return;
        await RuleEditorWindow.ShowAsync(owner, page.Gestures.AddRule());
        (sender as Control)?.Focus(NavigationMethod.Directional);
    });
}
