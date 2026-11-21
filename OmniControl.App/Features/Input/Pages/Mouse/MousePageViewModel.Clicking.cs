using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The "Repeated clicking" entry in the master action list and its on-screen badge. The engine is <see cref="RepeatedClicking"/> (owned by
/// the mouse service); a hotkey runs it as a toggle with its default settings, a button, gesture or rule with the settings it chose.
public sealed partial class MousePageViewModel
{
    private const string ClickBadgeElement = "Repeated clicking";

    private void RegisterClickingAction(IActionRegistry hotkeys)
    {
        var clicking = _mouse.Clicking;
        _osd.DeclareText(ClickBadgeElement);
        _followed.Follow<Action<long?>>(h => clicking.Progress += h, h => clicking.Progress -= h, clicks => Dispatcher.UIThread.Post(() =>
        {
            if (clicks is { } count) _osd.ShowMessage(ClickBadgeElement, new OsdMessage(OsdIcon.Play, "Clicking...", count.ToString()));
        }));
        hotkeys.Register(new HotkeyAction("mouse.repeatclick", "Repeated clicking", Title, "auto clicker autoclicker click repeat button hold toggle",
            _ => clicking.Toggle(OptionValues.None), group: "Clicks and wheel")
        {
            Accepts = ActionSettings.Clicking,
            RunConfigured = settings => clicking.Toggle(settings.Options ?? OptionValues.None),
            RunHeld = settings => clicking.StartHeld(settings.Options ?? OptionValues.None),
            SuggestedIcons = new[] { "mouse" },
        });
    }
}
