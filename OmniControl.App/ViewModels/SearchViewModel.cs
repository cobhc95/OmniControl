using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.ViewModels;

/// <summary>
/// Inline search results: one live control per matching setting, bound to the same
/// view-models as the tabs, so edits here apply immediately (mockup: design/tab-search.html).
/// </summary>
public partial class SearchViewModel : ViewModelBase
{
    public SearchViewModel(MainViewModel display, VolumeViewModel volume, MouseViewModel mouse,
        KeyboardViewModel keyboard, HotkeysViewModel hotkeys, SmartHomeViewModel home)
    {
        Display = display;
        Volume = volume;
        Mouse = mouse;
        Keyboard = keyboard;
        Hotkeys = hotkeys;
        Home = home;
    }

    public MainViewModel Display { get; }
    public VolumeViewModel Volume { get; }
    public MouseViewModel Mouse { get; }
    public KeyboardViewModel Keyboard { get; }
    public HotkeysViewModel Hotkeys { get; }
    public SmartHomeViewModel Home { get; }

    public event Action<string?>? OpenTabRequested;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _matchCount;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _showBrightness;
    [ObservableProperty] private bool _showLaptopBrightness;
    [ObservableProperty] private bool _showMonitors;
    [ObservableProperty] private bool _showContrast;
    [ObservableProperty] private bool _showMonitorVolume;
    [ObservableProperty] private bool _showInput;
    [ObservableProperty] private bool _showPower;
    [ObservableProperty] private bool _showCommand;
    [ObservableProperty] private bool _showRefresh;
    [ObservableProperty] private bool _showVolumeMaster;
    [ObservableProperty] private bool _showSpotify;
    [ObservableProperty] private bool _showChrome;
    [ObservableProperty] private bool _showMute;
    [ObservableProperty] private bool _showKeyBindings;
    [ObservableProperty] private bool _showSensitivity;
    [ObservableProperty] private bool _showProfiles;
    [ObservableProperty] private bool _showMouseButtons;
    [ObservableProperty] private bool _showNewBinding;
    [ObservableProperty] private bool _showHotkeyRows;
    [ObservableProperty] private bool _showKeyboardRebind;
    [ObservableProperty] private bool _showScenes;
    [ObservableProperty] private bool _showBulb;
    [ObservableProperty] private bool _showDesk;
    [ObservableProperty] private bool _showLightBar;
    [ObservableProperty] private bool _showColors;
    [ObservableProperty] private bool _showAddDevice;
    [ObservableProperty] private bool _showRetry;
    [ObservableProperty] private string? _firstFocusName;
    [ObservableProperty] private IReadOnlyList<string> _focusNames = Array.Empty<string>();

    [RelayCommand]
    private void GoTo(string? tab) => OpenTabRequested?.Invoke(tab);

    public void Clear()
    {
        Query = "";
        ShowBrightness = ShowLaptopBrightness = ShowMonitors = ShowContrast = ShowMonitorVolume
            = ShowInput = ShowPower = ShowCommand = ShowRefresh = ShowVolumeMaster
            = ShowSpotify = ShowChrome = ShowMute = ShowKeyBindings = ShowSensitivity
            = ShowProfiles = ShowMouseButtons = ShowNewBinding = ShowHotkeyRows = ShowKeyboardRebind
            = ShowScenes = ShowBulb = ShowDesk = ShowLightBar = ShowColors
            = ShowAddDevice = ShowRetry = false;
        MatchCount = 0;
        HasResults = false;
        FirstFocusName = null;
        FocusNames = Array.Empty<string>();
    }

    public void Update(string query)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        Query = query ?? "";
        if (q.Length == 0) { Clear(); return; }

        bool m(string title) => title.ToLowerInvariant().Contains(q);

        ShowBrightness = m("Brightness slider");
        ShowLaptopBrightness = m("Laptop panel") || m("laptop") || m("lcd") || m("wmi") || ShowBrightness;
        ShowMonitors = m("monitor") || m("laptop") || m("lcd") || m("display list") || m("select") || ShowBrightness;
        ShowContrast = m("Contrast slider");
        ShowMonitorVolume = m("Monitor volume");
        ShowInput = m("Input source");
        ShowPower = m("Power on") || m("Power off") || m("Factory reset") || m("power");
        ShowCommand = m("Run command") || m("Read capabilities") || m("command");
        ShowRefresh = m("Refresh monitors");
        ShowVolumeMaster = m("Volume slider") || m("Master volume") || m("volume") || m("vol");
        ShowSpotify = m("Spotify volume");
        ShowChrome = m("Chrome volume") || (m("chrome") && (m("volume") || m("vol")));
        ShowMute = m("Mute all / Unmute") || m("mute") || m("unmute");
        ShowKeyBindings = m("Change key binding") || m("key binding") || m("bind");
        ShowSensitivity = m("Sensitivity slider");
        ShowProfiles = m("Mouse profiles") || m("Middle click remap");
        ShowMouseButtons = m("button") || m("remap") || m("middle click");
        ShowNewBinding = m("New hotkey binding") || m("Toggle binding on/off");
        ShowHotkeyRows = m("toggle binding") || m("edit binding") || m("bindings");
        ShowKeyboardRebind = m("rebind") || m("media") || m("fn ") || m("fn") || m("keyboard") || m("caps") || m("f13") || m("f14") || m("play") || m("pause") || m("mute key");
        ShowScenes = m("Movie night scene") || m("Morning / Leaving scenes") || m("scene") || m("morning") || m("leaving");
        ShowBulb = m("Living room bulb") || m("bulb");
        ShowDesk = m("Desk lamp");
        ShowLightBar = m("Monitor light bar") || m("light bar");
        ShowColors = m("colour") || m("color") || m("light bar");
        ShowAddDevice = m("Add device") || m("smart plug") || m("device");
        ShowRetry = m("Retry connection") || m("bedroom plug");

        var cards = new[] {
            ShowBrightness, ShowLaptopBrightness, ShowMonitors, ShowContrast, ShowMonitorVolume,
            ShowInput, ShowPower, ShowCommand, ShowRefresh, ShowVolumeMaster, ShowSpotify,
            ShowChrome, ShowMute, ShowKeyBindings, ShowSensitivity, ShowProfiles,
            ShowMouseButtons, ShowNewBinding, ShowHotkeyRows, ShowKeyboardRebind, ShowScenes, ShowBulb,
            ShowDesk, ShowLightBar, ShowColors, ShowAddDevice, ShowRetry
        };
        MatchCount = cards.Count(x => x);
        HasResults = MatchCount > 0;

        var names = new List<string>();
        if (ShowBrightness) names.Add("BrightnessSlider");
        if (ShowLaptopBrightness) names.Add("LaptopBrightnessSlider");
        if (ShowMonitors) names.Add("MonitorsList");
        if (ShowContrast) names.Add("ContrastSlider");
        if (ShowMonitorVolume) names.Add("MonitorVolumeSlider");
        if (ShowInput) names.Add("InputSourceButton");
        if (ShowPower) names.Add("PowerOnButton");
        if (ShowCommand) names.Add("CommandBox");
        if (ShowRefresh) names.Add("RefreshButton");
        if (ShowVolumeMaster) names.Add("MasterSlider");
        if (ShowSpotify) names.Add("SpotifySlider");
        if (ShowChrome) names.Add("ChromeSlider");
        if (ShowMute) names.Add("MuteButton");
        if (ShowKeyBindings) names.Add("VolumeRowsList");
        if (ShowSensitivity) names.Add("SensitivitySlider");
        if (ShowProfiles) names.Add("ProfilesList");
        if (ShowMouseButtons) names.Add("MouseButtonsList");
        if (ShowNewBinding) names.Add("NewBindingButton");
        if (ShowHotkeyRows) names.Add("HotkeysRowsList");
        if (ShowKeyboardRebind) names.Add("KeyRebindList");
        if (ShowScenes) names.Add("ScenesList");
        if (ShowBulb) names.Add("BulbToggle");
        if (ShowDesk) names.Add("DeskToggle");
        if (ShowLightBar) names.Add("LightBarToggle");
        if (ShowAddDevice) names.Add("AddDeviceButton");
        if (ShowRetry) names.Add("RetryButton");
        FocusNames = names;
        FirstFocusName = names.Count > 0 ? names[0] : null;
    }
}
