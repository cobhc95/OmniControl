using Avalonia.Controls;

namespace OmniControl.App.Pages.AppSettingsPage;

public partial class AppSettingsPage : UserControl
{
    public AppSettingsPage()
    {
        InitializeComponent();
        RowEditing.Attach(SettingsList);
    }
}
