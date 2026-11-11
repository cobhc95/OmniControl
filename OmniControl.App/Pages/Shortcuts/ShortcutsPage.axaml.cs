using Avalonia.Controls;

namespace OmniControl.App.Pages.Shortcuts;

public partial class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, GroupsHost);
    }
}
