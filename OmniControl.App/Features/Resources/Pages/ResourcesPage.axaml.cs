namespace OmniControl.App.Features.Resources.Pages;

/// Wiring only: the page's keyboard (groups and cards of both tabs, Ctrl+Tab between tabs).
public partial class ResourcesPage : UserControl
{
    public ResourcesPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
        PageTabsView.Attach(this);
    }
}
