using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Traffic.Pages;

namespace OmniControl.App.Features.Traffic;

/// Network traffic control: the programs' limits and blocks (<see cref="NetworkLimits"/>), their actions, the adapters' watch, the reader of
/// the programs' traffic and the page. Its rules live in resources.json, so it is given the Resources module's one settings instance.
public sealed class TrafficModule : IFeatureModule
{
    private const string Title = TrafficPageViewModel.PageTitle;

    private readonly ResourcesSettings _settings;
    private FeatureContext? _context;
    private NetworkLimits? _limits;
    private TrafficMonitor? _monitor;
    private int _programs;

    public TrafficModule(ResourcesSettings settings) => _settings = settings;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Traffic };

    public void Register(FeatureContext context)
    {
        (_context, _programs) = (context, 0);
        var monitor = _monitor = new TrafficMonitor(() => _limits?.StartTraffic() ?? throw new IOException("Network traffic control is off."));
        var limits = _limits = new NetworkLimits(_settings, context.Report, id => monitor.Seen.GetValueOrDefault(id));
        limits.RegisterActions(context.Actions, Title, () => monitor.Seen.Keys.Concat(_settings.Setup.Apps.Select(r => r.Id)).Distinct().Select(id => new HotkeyDevice(id, id)).ToList());
        context.Hardware.Register(new HardwareWatch("resources.download-adapters", "Network adapters (download limits)",
            HardwareSignal.Device | HardwareSignal.Resume, limits.RefreshAdapters));
        // A program seen for the first time joins the actions' list.
        monitor.Updated += () =>
        {
            if (monitor.Seen.Count == _programs) return;
            _programs = monitor.Seen.Count;
            limits.RefreshPrograms();
        };
        context.Imports.OnReload(DataStores.Resources, _settings.Reload);
        // Each on its own at the stage that puts Windows back: download shaping ends, the rules leave Windows, then the traffic session stops.
        context.Shutdown.Add(ShutdownStage.Resources, "download limits", limits.StopDownloads);
        context.Shutdown.Add(ShutdownStage.Resources, "network limits", limits.Dispose);
        context.Shutdown.Add(ShutdownStage.Resources, "network traffic reader", monitor.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(TrafficPageViewModel.Info, () => new TrafficPageViewModel(_monitor!, _settings, _limits!, _context!.AppScopes, _context.AppScopeGate, _context.Report)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<TrafficPageViewModel, TrafficPage>(),
        ViewTemplate.For<AppNetItem, AppNetRow>(),
    };
}
