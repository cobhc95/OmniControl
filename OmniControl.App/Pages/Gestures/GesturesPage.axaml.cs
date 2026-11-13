using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Gestures;

public partial class GesturesPage : UserControl
{
    public GesturesPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }

    /// <summary>"Add rule": the page makes the rule, this opens its editor; focus comes back to the button.</summary>
    private async void OnAddRule(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GesturesPageViewModel page || TopLevel.GetTopLevel(this) is not Window owner) return;
        await RuleEditorWindow.ShowAsync(owner, page.AddRule());
        (sender as Control)?.Focus(NavigationMethod.Directional);
    }
}
