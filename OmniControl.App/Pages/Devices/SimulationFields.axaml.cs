using Avalonia.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>Wiring only: the emulator menu is given its rows when it opens (a pop-down's content does not inherit the data context) and closes after a pick.</summary>
public partial class SimulationFields : UserControl
{
    private Flyout? _menu;
    private Flyout? _keep;

    public SimulationFields()
    {
        InitializeComponent();
        EmulatorRows.Picked += (_, _) => _menu?.Hide();
        KeepMenu.Picked += (_, _) => _keep?.Hide();
    }

    private void OnKeepOpened(object? sender, Flyout flyout)
    {
        _keep = flyout;
        if (DataContext is not SimulationViewModel model) return;
        KeepMenu.Bind(model, withActions: false);
    }

    private void OnMenuOpened(object? sender, Flyout flyout)
    {
        _menu = flyout;
        if (DataContext is not SimulationViewModel model) return;
        EmulatorRows.ItemsSource = model.EmulatorOptions;
    }
}
