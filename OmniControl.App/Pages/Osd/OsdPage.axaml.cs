using Avalonia.Controls;

namespace OmniControl.App.Pages.Osd;

public partial class OsdPage : UserControl
{
    public OsdPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }
}
