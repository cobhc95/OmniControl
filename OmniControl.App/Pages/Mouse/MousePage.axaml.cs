using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Mouse;

public partial class MousePage : UserControl
{
    public MousePage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }
}
