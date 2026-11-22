using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Traffic;
using OmniControl.App.Features.Traffic.Pages;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Network Traffic Control page over <see cref="SampleLimits"/> and a <see cref="SampleTraffic"/> session: the real page, rows and
    /// reader, no firewall rule, driver, service or event trace; the programs are made up and have no file, so no icon or name is read.
    private static void NetworkTraffic(Rig rig, Camera camera) => NetworkTraffic(rig, camera, "network-traffic-control",
        NetRule("Game launcher", download: 5120), NetRule("System update service", download: 1024));

    /// The same page where the Game launcher's upload is blocked (its Upload limit chip says Block).
    private static void NetworkTrafficBlock(Rig rig, Camera camera) => NetworkTraffic(rig, camera, "traffic-block",
        new AppNetRule { Id = "Game launcher", DownloadOn = true, DownloadKbps = 5120, UploadBlocked = true }, NetRule("System update service", download: 1024));

    private static void NetworkTraffic(Rig rig, Camera camera, string name, params AppNetRule[] rules)
    {
        Use(ViewTemplate.For<TrafficPageViewModel, TrafficPage>(), ViewTemplate.For<AppNetItem, AppNetRow>(), ViewTemplate.For<AllAppsItem, AllAppsRow>());
        var settings = ResourcesSettings.Load();
        var limits = new SampleLimits(rules);
        var monitor = new TrafficMonitor(() => new SampleTraffic());
        var page = new TrafficPageViewModel(monitor, settings, limits, new SampleNetworks(),
            new ProviderChoice($"{TrafficPageViewModel.PageTitle} presets", new TrafficPresets(settings), () => { }, withCustom: true), Rig.Report);
        page.OnOnScreenChanged(true); // the reader starts its session, then reads one interval later
        Camera.Pump((int)TimeSpan.FromSeconds(settings.Setup.ReadEverySeconds * 1.5).TotalMilliseconds);
        camera.Page(page, name);
        page.Dispose();
        monitor.Dispose();
    }

    private static AppNetRule NetRule(string id, int download) => new() { Id = id, DownloadOn = true, DownloadKbps = download };

    /// The programs' rules held in memory: nothing reaches Windows.
    private sealed class SampleLimits(params AppNetRule[] rules) : INetworkLimits
    {
        public AppNetRule? Rule(NetScope scope, string appId) => rules.FirstOrDefault(r => r.Id == appId);
        public void Set(NetScope scope, IEnumerable<string> appIds, Action<AppNetRule> change)
        {
            foreach (var id in appIds) change(Rule(scope, id) ?? new AppNetRule { Id = id });
        }
        public bool HasOwn(NetScope scope) => false;
        public void UseParent(NetScope scope) { }
        public OmniControl.App.Shared.Contracts.FeatureRuleView? RuleOf(NetScope place) => null;
        public bool InEffect(NetScope place) => false;
        public void EditRule(NetScope place) { }
        public string? UploadScopeLine => null;
        public bool PacketFilterOn { get; private set; } = true;
        public void SetPacketFilter(bool on) => PacketFilterOn = on;
        public event Action? Changed { add { } remove { } }
        public bool Available => true;
        public bool CanInstallService => false;
        public string UnavailableLine => string.Empty;
        public NetworkLimits.ServiceState Service => NetworkLimits.ServiceState.Running;
        public NetworkLimits.DownloadState Download => NetworkLimits.DownloadState.Running(false);
        public void InstallPacketFilter() { }
        public Task InstallServiceAsync() => Task.CompletedTask;
        public void Recheck() { }
    }

    /// No adapter or network: the Adapter choice offers All adapters only.
    private sealed class SampleNetworks : INetworkWatch
    {
        public NetworkSnapshot Now => NetworkSnapshot.Empty;
        public event Action? Changed { add { } remove { } }
        public IDisposable Need() => new HoldCount().Take();
    }

    /// Four made-up programs with steady speeds (bytes per second); each take is the bytes of the time since the last, as the reader divides them.
    private sealed class SampleTraffic : ITrafficSession
    {
        private static readonly (string App, double Down, double Up)[] Speeds =
        {
            ("Web browser", 1.4 * 1024 * 1024, 62 * 1024),
            ("Game launcher", 8.2 * 1024 * 1024, 110 * 1024),
            ("Music player", 320 * 1024, 4 * 1024),
            ("System update service", 2.1 * 1024 * 1024, 0),
        };

        private DateTime _last = DateTime.UtcNow;

        public string? Stopped => null;

        public IReadOnlyList<AppTraffic> Take()
        {
            var now = DateTime.UtcNow;
            var seconds = (now - _last).TotalSeconds;
            _last = now;
            return Speeds.Select(s => new AppTraffic(s.App, string.Empty, (long)(s.Down * seconds), (long)(s.Up * seconds))).ToList();
        }

        public void Dispose() { }
    }
}
