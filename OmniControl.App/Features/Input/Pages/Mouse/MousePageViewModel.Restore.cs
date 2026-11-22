namespace OmniControl.App.Features.Input.Pages.Mouse;

public sealed partial class MousePageViewModel
{
    /// "Restore defaults" or its undo (the module's, <see cref="InputModule"/>) changed the services: the gestures, rules and pointer settings show what they hold now.
    public void ShowRestored()
    {
        Gestures.ShowReplaced();
        LoadPointerSettings();
    }
}
