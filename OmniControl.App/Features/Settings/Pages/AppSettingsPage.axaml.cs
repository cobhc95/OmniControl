namespace OmniControl.App.Features.Settings.Pages;

public partial class AppSettingsPage : UserControl
{
    public AppSettingsPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }
}
