using Avalonia.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>Wiring only: a pop-down's content does not inherit the data context, so the owner gives the rows in <see cref="Bind"/> each time the menu opens; any pick raises <see cref="Picked"/> so the owner can close it.</summary>
public partial class KeepRunningMenu : UserControl
{
    public KeepRunningMenu()
    {
        InitializeComponent();
        ActionRows.Picked += (_, _) => Picked?.Invoke(this, EventArgs.Empty);
        KeepRows.Picked += (_, _) => Picked?.Invoke(this, EventArgs.Empty);
        KeepSwitches.Picked += (_, _) => Picked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Picked;

    /// <param name="withActions">Show the Close now / Start and Show rows at the top (the emulator chip's menu).</param>
    public void Bind(SimulationViewModel model, bool withActions)
    {
        IdleRow.DataContext = model;
        KeepRows.ItemsSource = model.KeepOptions;
        KeepSwitches.ItemsSource = model.KeepSwitches;
        ActionPart.IsVisible = withActions;
        if (withActions) ActionRows.ItemsSource = model.EmulatorActions;
    }
}
