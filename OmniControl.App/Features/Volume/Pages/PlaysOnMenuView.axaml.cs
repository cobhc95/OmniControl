namespace OmniControl.App.Features.Volume.Pages;

/// Wiring only: tells the host (the Wiring cell, the Mixer's Plays on chip) that an entry or the Switch was pressed, so it closes the menu.
public partial class PlaysOnMenuView : UserControl
{
    public PlaysOnMenuView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, (_, _) => Picked?.Invoke());
    }

    /// An entry or the Switch was pressed.
    public event Action? Picked;
}
