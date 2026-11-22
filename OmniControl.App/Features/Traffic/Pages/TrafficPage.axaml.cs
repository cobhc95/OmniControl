namespace OmniControl.App.Features.Traffic.Pages;

/// Wiring only: the page's keyboard (its group and rows).
public partial class TrafficPage : UserControl
{
    public TrafficPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }
}
