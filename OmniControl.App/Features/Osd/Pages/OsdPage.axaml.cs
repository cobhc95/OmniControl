namespace OmniControl.App.Features.Osd.Pages;

public partial class OsdPage : UserControl
{
    public OsdPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }
}
