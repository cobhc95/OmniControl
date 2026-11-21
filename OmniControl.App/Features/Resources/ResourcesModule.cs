using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Features.Resources.Pages;
using OmniControl.App.Features.Resources.Weather;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources;

/// The windows the taskbar gadget's menu and the page open (the module owns them).
public interface IResourcesWindows
{
    /// Opens the page at its Readings tab, at its Network traffic control group.
    void OpenApps();

    /// Opens the page at its Taskbar gadget tab.
    void OpenGadgetSettings();
}

/// Resources: the poller and its states, network limits, the taskbar gadget with its weather and layouts, presets, the Open action and the page.
public sealed class ResourcesModule : IFeatureModule, IResourcesWindows
{
    public const string OpenActionId = "resources.open";
    private const string Title = ResourcesPageViewModel.PageTitle;

    private readonly ResourcesSettings _settings = ResourcesSettings.Load();
    private FeatureContext? _context;
    private ResourceMonitor? _monitor;
    private NetworkLimits? _limits;
    private TaskbarGadget? _gadget;
    private WeatherService? _weather;
    private GadgetLayouts? _layouts;
    private ResourcePresets? _presets;
    private ResourcesPageViewModel? _page;
    private int _programs;

    public void Register(FeatureContext context)
    {
        _context = context;
        _settings.Setup.Gadget.MoveOldClickDefault();
        _settings.Setup.Gadget.Complete();
        var monitor = _monitor = new ResourceMonitor(context.States, () => TimeSpan.FromSeconds(_settings.Setup.ReadEverySeconds), () => _settings.Setup.Adapter,
            () => _settings.Setup.Sensor, () => _limits!.StartTraffic(), Title);
        var limits = _limits = new NetworkLimits(_settings, context.Report, id => monitor.Seen.GetValueOrDefault(id));
        limits.RegisterActions(context.Actions, Title, () => monitor.Seen.Keys.Concat(_settings.Setup.Apps.Select(r => r.Id)).Distinct().Select(id => new HotkeyDevice(id, id)).ToList());
        context.Hardware.Register(new HardwareWatch("resources.download-adapters", "Network adapters (download limits)",
            HardwareSignal.Device | HardwareSignal.Resume, limits.RefreshAdapters));
        context.Hardware.Register(new HardwareWatch("resources.disks", "Physical disks (gadget readings)", HardwareSignal.Device | HardwareSignal.Resume,
            monitor.RefreshDisks, LooksStale: () => !monitor.DisksKnown, Describe: monitor.DisksText));
        monitor.Updated += () =>
        {
            if (monitor.Seen.Count == _programs) return;
            _programs = monitor.Seen.Count;
            limits.RefreshPrograms(); // a program seen for the first time joins the actions' list
        };
        var weather = _weather = new WeatherService(_settings, context.Actions, context.States, new WindowsCredentialStore(), Title);
        var layouts = _layouts = new GadgetLayouts(_settings);
        var gadget = _gadget = new TaskbarGadget(monitor, weather, _settings, layouts, context.Runner, context.States, context.Report, this);
        gadget.Register(context.Actions, context.Hardware, Title);
        _presets = new ResourcePresets(_settings);
        context.Presets.Register(new PresetGroup(ResourcePresets.GroupId, Title, Title, _presets));
        context.Presets.Register(new PresetGroup(GadgetLayouts.GroupId, Title, "Gadget layouts", _layouts));
        context.Actions.Register(new HotkeyAction(OpenActionId, "Open Resources", Title, "resources cpu gpu memory network temperature open page", _ => OpenPage())
        {
            SuggestedIcons = new[] { "device-desktop-analytics" },
        });
        context.Imports.OnReload(DataStores.Resources, () =>
        {
            var fresh = ResourcesSettings.Load();
            // the preset lists keep the lists they were given: they are refilled, never replaced
            _settings.Presets.Clear();
            _settings.Presets.AddRange(fresh.Presets);
            fresh.Presets = _settings.Presets;
            _settings.Layouts.Clear();
            _settings.Layouts.AddRange(fresh.Layouts);
            fresh.Layouts = _settings.Layouts;
            SettingsCopy.Into(_settings, fresh);
            _settings.Commit();
            return true;
        });
        // Each on its own at the stage that puts Windows back: download shaping ends and the rules leave Windows first, then the traffic
        // session stops, then the gadget closes.
        context.Shutdown.Add(ShutdownStage.Resources, "download limits", limits.StopDownloads);
        context.Shutdown.Add(ShutdownStage.Resources, "network limits", limits.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "resources poller", monitor.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "taskbar gadget", gadget.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "gadget weather", weather.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(Title, () => _page = new ResourcesPageViewModel(_monitor!, _settings, _limits!,
            new ProviderChoice("Resources presets", _presets!, () => _context!.OpenPresetManager(Title, ResourcePresets.GroupId), withCustom: true),
            _context!.AppScopes, _context.AppScopeGate,
            new GadgetSettingsModel(_settings, _context.Actions, _gadget!, _layouts!, () => _context.OpenPresetManager(Title, GadgetLayouts.GroupId), _weather!,
                _context.IconPicker.ChooseAsync, ChooseActionAsync), _context.Report)),
    };

    /// The Choose-the-action window over the main window (the gadget's click actions).
    private static Task<PickResult?> ChooseActionAsync(PickerModel picker, string title) =>
        OwnedWindow.MainWindow is { } owner ? PickerWindow.ChooseAsync(owner, title, picker) : Task.FromResult<PickResult?>(null);

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<ResourcesPageViewModel, ResourcesPage>(),
        ViewTemplate.For<GadgetSettingsModel, GadgetTabView>(),
        ViewTemplate.For<ReadingCard, ReadingRow>(),
        ViewTemplate.For<AppNetItem, AppNetRow>(),
        ViewTemplate.For<WeatherPlaceItem, WeatherPlaceRow>(),
    };

    private void OpenPage() => OpenAt(null);

    public void OpenApps()
    {
        OpenAt(ResourcesPageViewModel.ReadingsTab);
        _page?.RevealApps();
    }

    public void OpenGadgetSettings() => OpenAt(ResourcesPageViewModel.GadgetTab);

    private void OpenAt(string? tab)
    {
        AppLifecycle.RequestShow("the Resources page");
        _context!.OpenPage(Title, tab);
    }
}
