using Avalonia.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>Wiring only: the emulator menu is given its rows when it opens (a pop-down's content does not inherit the data context) and closes after a pick.</summary>
public partial class SimulationFields : UserControl
{
    private Flyout? _menu;

    public SimulationFields()
    {
        InitializeComponent();
        EmulatorRows.Picked += (_, _) => _menu?.Hide();
    }

    private void OnMenuOpened(object? sender, Flyout flyout)
    {
        _menu = flyout;
        EmulatorRows.ItemsSource = (DataContext as SimulationViewModel)?.EmulatorOptions;
    }
}
