using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Shortcuts;

public partial class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, GroupsHost);
    }
}
