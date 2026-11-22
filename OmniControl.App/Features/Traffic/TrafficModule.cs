using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Traffic.Pages;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Traffic;

/// Network Traffic Control: the programs' limits and blocks (<see cref="NetworkLimits"/>), their actions, the adapters' watch, THE network
/// watch (<see cref="Networks"/>), the reader of the programs' traffic, its presets and the page; its settings are traffic.json.
public sealed class TrafficModule : IFeatureModule
{
    private const string Title = TrafficPageViewModel.PageTitle;

    private readonly TrafficSettings _settings = TrafficSettings.Load();
    private readonly Func<TimeSpan> _readEvery;
    private FeatureContext? _context;
    private NetworkLimits? _limits;
    private TrafficMonitor? _monitor;
    private TrafficPresets? _presets;
    private int _programs;

    /// <param name="readEvery">How often the programs' traffic is read while shown (the Resources page's "Read every").</param>
    public TrafficModule(Func<TimeSpan> readEvery) => _readEvery = readEvery;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Traffic };

    /// THE network watch (adapters, saved networks, the one each adapter is on, metered), made at <see cref="Register"/>.
    public INetworkWatch? Networks { get; private set; }

    public void Register(FeatureContext context)
    {
        (_context, _programs) = (context, 0);
        var monitor = _monitor = new TrafficMonitor(() => _limits?.StartTraffic() ?? throw new IOException($"{Title} is off."));
        var networks = Networks = new NetworkWatch(context.Hardware);
        var states = new NetworkStates(context.States, networks, Title);
        var limits = _limits = new NetworkLimits(_settings, context.Report, id => monitor.Seen.GetValueOrDefault(id), networks, context.Rules);
        _presets = new TrafficPresets(_settings);
        context.Presets.Register(new PresetGroup(TrafficPresets.GroupId, Title, Title, _presets));
        limits.RegisterActions(context.Actions, Title, () => monitor.Seen.Keys.Concat(_settings.Apps.Select(r => r.Id)).Distinct().Select(id => new HotkeyDevice(id, id)).ToList());
        context.Hardware.Register(new HardwareWatch("resources.download-adapters", "Network adapters (download limits)",
            HardwareSignal.Device | HardwareSignal.Resume, limits.RefreshAdapters));
        // A program seen for the first time joins the actions' list.
        monitor.Updated += () =>
        {
            if (monitor.Seen.Count == _programs) return;
            _programs = monitor.Seen.Count;
            limits.RefreshPrograms();
        };
        context.Imports.OnReload(DataStores.Traffic, _settings.Reload);
        // Each on its own at the stage that puts Windows back: download shaping ends, the rules leave Windows, then the traffic session stops.
        context.Shutdown.Add(ShutdownStage.Resources, "download limits", limits.StopDownloads);
        context.Shutdown.Add(ShutdownStage.Resources, "network limits", limits.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "network traffic reader", monitor.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "network states", states.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(TrafficPageViewModel.Info, () => new TrafficPageViewModel(_monitor!, _settings, _limits!, Networks!,
            new ProviderChoice($"{Title} presets", _presets!, () => _context!.OpenPresetManager(Title, TrafficPresets.GroupId), withCustom: true), _readEvery, _context!.Report),
            () => TrafficPageViewModel.Words(_settings, _limits)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<TrafficPageViewModel, TrafficPage>(),
        ViewTemplate.For<AppNetItem, AppNetRow>(),
        ViewTemplate.For<AllAppsItem, AllAppsRow>(),
    };
}
