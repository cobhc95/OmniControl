using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Features.Resources.Pages;
using OmniControl.App.Features.Resources.Weather;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources;

/// The windows the taskbar gadget's menu opens.
public interface IResourcesWindows
{
    /// Opens the Network Traffic Control page.
    void OpenApps();

    /// Opens the page at its Taskbar gadget tab.
    void OpenGadgetSettings();

    /// Network Traffic Control runs (the gadget's menu offers it).
    bool HasTraffic { get; }

    /// The Network Traffic Control page's title, for the menu's link.
    string TrafficTitle { get; }
}

/// Resources: the poller and its states, the taskbar gadget with its weather and layouts, presets, the Open action and the page. Its settings
/// file (resources.json) holds Network Traffic Control's rules too, so the shell gives the same <see cref="Settings"/> to that module.
public sealed class ResourcesModule : IFeatureModule, IResourcesWindows
{
    public const string OpenActionId = "resources.open";
    private const string Title = ResourcesPageViewModel.PageTitle;

    private readonly string _trafficPage;
    private FeatureContext? _context;
    private ResourceMonitor? _monitor;
    private TaskbarGadget? _gadget;
    private WeatherService? _weather;
    private GadgetLayouts? _layouts;
    private ResourcePresets? _presets;

    /// <param name="trafficPage">The Network Traffic Control page's title (the gadget's menu opens it).</param>
    public ResourcesModule(string trafficPage) => _trafficPage = trafficPage;

    /// resources.json, loaded once for both modules that keep their settings in it.
    public ResourcesSettings Settings { get; } = ResourcesSettings.Load();

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Resources };

    public bool HasTraffic => _context?.Features.IsOn(FeatureList.Traffic) == true;

    public string TrafficTitle => _trafficPage;

    public void Register(FeatureContext context)
    {
        _context = context;
        Settings.Setup.Gadget.MoveOldClickDefault();
        Settings.Setup.Gadget.Complete();
        var monitor = _monitor = new ResourceMonitor(context.States, () => TimeSpan.FromSeconds(Settings.Setup.ReadEverySeconds), () => Settings.Setup.Adapter,
            () => Settings.Setup.Sensors, () => Settings.Setup.Gpus, () => Settings.Setup.SpeedAdapters, Title);
        context.Hardware.Register(new HardwareWatch("resources.disks", "Graphics adapters and physical disks (readings)", HardwareSignal.Device | HardwareSignal.Resume,
            monitor.RefreshHardware, LooksStale: () => !monitor.HardwareKnown, Describe: monitor.HardwareText));
        var weather = _weather = new WeatherService(Settings, context.Actions, context.States, new WindowsCredentialStore(), Title);
        var layouts = _layouts = new GadgetLayouts(Settings);
        var gadget = _gadget = new TaskbarGadget(monitor, weather, Settings, layouts, context.Runner, context.States, context.Report, this);
        gadget.Register(context.Actions, context.Hardware, Title);
        _presets = new ResourcePresets(Settings);
        context.Presets.Register(new PresetGroup(ResourcePresets.GroupId, Title, Title, _presets));
        context.Presets.Register(new PresetGroup(GadgetLayouts.GroupId, Title, "Gadget layouts", layouts));
        context.Actions.Register(new HotkeyAction(OpenActionId, "Open Resources", Title, "resources cpu gpu memory network temperature open page", _ => OpenAt(null))
        {
            SuggestedIcons = new[] { "device-desktop-analytics" },
        });
        context.Imports.OnReload(DataStores.Resources, Settings.Reload);
        // Each on its own at the stage that puts Windows back: the poller stops, then the gadget closes.
        context.Shutdown.Add(ShutdownStage.Resources, "resources poller", monitor.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "taskbar gadget", gadget.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "gadget weather", weather.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(ResourcesPageViewModel.Info, () => new ResourcesPageViewModel(_monitor!, Settings,
            new ProviderChoice("Resources presets", _presets!, () => _context!.OpenPresetManager(Title, ResourcePresets.GroupId), withCustom: true),
            new GadgetSettingsModel(Settings, _context!.Actions, _gadget!, _layouts!, () => _context.OpenPresetManager(Title, GadgetLayouts.GroupId), _weather!,
                _context.IconPicker.ChooseAsync, ChooseActionAsync)), () => ResourcesPageViewModel.Words(_monitor)),
    };

    /// The Choose-the-action window over the main window (the gadget's click actions).
    private static Task<PickResult?> ChooseActionAsync(PickerModel picker, string title) =>
        OwnedWindow.MainWindow is { } owner ? PickerWindow.ChooseAsync(owner, title, picker) : Task.FromResult<PickResult?>(null);

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<ResourcesPageViewModel, ResourcesPage>(),
        ViewTemplate.For<GadgetSettingsModel, GadgetTabView>(),
        ViewTemplate.For<ReadingCard, ReadingRow>(),
        ViewTemplate.For<WeatherPlaceItem, WeatherPlaceRow>(),
    };

    public void OpenApps()
    {
        AppLifecycle.RequestShow($"the {_trafficPage} page");
        _context!.OpenPage(_trafficPage, null);
    }

    public void OpenGadgetSettings() => OpenAt(ResourcesPageViewModel.GadgetTab);

    private void OpenAt(string? tab)
    {
        AppLifecycle.RequestShow("the Resources page");
        _context!.OpenPage(Title, tab);
    }
}
