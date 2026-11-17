using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Osd;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The "Repeated clicking" entry in the master action list and its on-screen badge. The engine is <see cref="RepeatedClicking"/> (owned by
/// the mouse service); a hotkey runs it as a toggle with its default settings, a button, gesture or rule with the settings it chose.
/// </summary>
public sealed partial class MousePageViewModel
{
    private const string ClickBadgeElement = "Repeated clicking";

    private void RegisterClickingAction(HotkeyRegistry hotkeys)
    {
        var clicking = _mouse.Clicking;
        _osd.DeclareText(ClickBadgeElement);
        clicking.Progress += clicks => Dispatcher.UIThread.Post(() =>
        {
            if (clicks is { } count) _osd.ShowMessage(ClickBadgeElement, new OsdMessage(OsdIcon.Play, "Clicking...", count.ToString()));
        });
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
