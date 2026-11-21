using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Settings.Pages;

namespace OmniControl.App.Shell;

// App settings' entries in the master action list, registered at start-up where its page used to be made, so the page itself is made only
// when it is opened. The page's own work (the plug-in scan with its progress line) still runs through the page.
public sealed partial class ShellViewModel
{
    private void RegisterAppActions()
    {
        const string home = AppSettingsPageViewModel.Name;
        string RunBy() => RunChain.Describe(_hotkeys.Chain);
        _hotkeys.Register(new HotkeyAction("app.show", "Open OmniControl", home, "show window open tray restore",
            _ => AppLifecycle.RequestShow($"the action Open OmniControl, run by {RunBy()}")) { NeverBlock = true, SuggestedIcons = new[] { "app", "home" } });
        _hotkeys.Register(new HotkeyAction("app.toggle", "Show or hide OmniControl", home, "show hide toggle window tray minimise",
            _ => AppLifecycle.RequestToggle($"the action Show or hide OmniControl, run by {RunBy()}"))
            { NeverBlock = true, DefaultKey = "Alt+X", SuggestedIcons = new[] { "app", "home" } });
        _hotkeys.Register(new HotkeyAction("app.shortcuts", "Keyboard shortcuts", home, "keyboard shortcuts keys help list f1", _ => ShowKeyboardShortcuts())
            { SuggestedIcons = new[] { "keyboard", "help" } });
        _hotkeys.Register(new HotkeyAction("app.quit", "Quit OmniControl", home, "exit quit close stop tray", _ => AppLifecycle.RequestQuit()) { SuggestedIcons = new[] { "power", "lock" } });
        _hotkeys.Register(new HotkeyAction("app.updates.check", "Check for updates", home, "update updates upgrade new version release github check", devices => { _ = _updates.CheckAsync(); })
        {
            SuggestedIcons = new[] { "download", "refresh" },
        });
        _hotkeys.Register(new HotkeyAction("app.plugins.scan", "Scan for plug-ins", home, "plug-in plugin vst vst2 vst3 scan search folders",
            _ => (_appSettings.Page as AppSettingsPageViewModel)?.StartScan()));
    }
}
