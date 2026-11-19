using Avalonia.Controls.ApplicationLifetimes;

namespace OmniControl.App.Shared.Services;

/// The one way OmniControl shows a secondary window that is not a dialog (FX windows, popped-out plug-in editors, About): owned by
/// the window it belongs to (the main window unless another is given), so Windows keeps it above that window while either is
/// dragged or clicked, without making it always-on-top over other programs. An unowned window is an equal top-level window,
/// which the main window could cover. Windows hides owned windows while their owner is minimised.
/// Dialogs keep using <c>ShowDialog(owner)</c>, which owns them the same way.
public static class OwnedWindow
{
    public static void Show(Window window, Window? owner = null)
    {
        owner ??= MainWindow;
        // Avalonia refuses a hidden owner (the main window in the tray): the window then opens on its own.
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, window)) window.Show(owner);
        else window.Show();
    }

    /// Shows <paramref name="dialog"/> as a dialog of the main window and returns its answer (<c>Close(true)</c>); a window closed any
    /// other way answers false. Dialogs are opened from the main window, so it is showing; if not, nothing opens and the answer is false.
    public static async Task<bool> ShowDialogAsync(Window dialog) =>
        MainWindow is { IsVisible: true } owner && await dialog.ShowDialog<bool?>(owner) == true;

    /// The main window (also where a picker opened by a service rather than from a control appears).
    internal static Window? MainWindow => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
