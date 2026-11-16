using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: tells the host (the Wiring cell, the Mixer's Plays on chip) that an entry or the Switch was pressed, so it closes the menu.</summary>
public partial class PlaysOnMenuView : UserControl
{
    public PlaysOnMenuView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, (_, _) => Picked?.Invoke());
    }

    /// <summary>An entry or the Switch was pressed.</summary>
    public event Action? Picked;
}
