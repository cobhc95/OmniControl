namespace OmniControl.App.Shared.Ui.Pages;

/// THE titles of the pages whose saved data is an area of the settings window: the page and the master list of saved data
/// (<see cref="DataStores"/>, which sits below the features) both read them here, so an area is always named like its page.
public static class PageTitles
{
    public const string Displays = "Displays", Volume = "Audio and Volume", SmartDevices = "Smart Devices", Mouse = "Mouse and Trackpad",
        Automation = "Automation", Resources = "Resources", ActionsAndHotkeys = "Actions and Hotkeys", Osd = "OSD", AppSettings = "App settings",
        OtherPcs = "Other PCs", Traffic = "Network Traffic Control";
}
