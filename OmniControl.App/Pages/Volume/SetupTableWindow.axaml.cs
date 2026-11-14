using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: shows a <see cref="SetupTable"/> beside the main window; Esc or the window's X closes it.</summary>
public partial class SetupTableWindow : Window
{
    public SetupTableWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The page's way to show the table: one window at a time (brought to the front when it is already open), kept above the main
    /// window (<see cref="OwnedWindow"/>). The open window is remembered in the returned opener, not in a static.
    /// </summary>
    public static Action<SetupTable> Opener()
    {
        SetupTableWindow? open = null;
        return table =>
        {
            if (open is not null)
            {
                open.Activate();
                return;
            }
            open = new SetupTableWindow { DataContext = table };
            open.Closed += (_, _) => open = null;
            OwnedWindow.Show(open);
        };
    }
}
