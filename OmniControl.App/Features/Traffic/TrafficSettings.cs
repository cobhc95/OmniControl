using System.Text.Json.Serialization;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Traffic;

/// One program's network rule: an upload and a download limit in KB/s (kept while switched off, so the chip can turn them on again), the block of both
/// ways, and the block of upload or download alone (a block replaces that direction's limit while it is on; files from before have none).
public sealed class AppNetRule
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int UploadKbps { get; set; }
    public bool UploadOn { get; set; }
    public int DownloadKbps { get; set; }
    public bool DownloadOn { get; set; }
    public bool Blocked { get; set; }
    public bool UploadBlocked { get; set; }
    public bool DownloadBlocked { get; set; }

    /// True when the rule asks nothing of Windows (it may be forgotten once Windows was told).
    [JsonIgnore] public bool IsPlain => !UploadOn && !DownloadOn && !Blocked && !UploadBlocked && !DownloadBlocked;

    public AppNetRule Copy() => (AppNetRule)MemberwiseClone();
}

/// One adapter's own network rules (<see cref="Network"/> empty), or one network's on that adapter: the most specific wins (the network's,
/// else its adapter's, else <see cref="TrafficSettings.Apps"/>). The names are kept so an adapter or network that is gone still shows.
public sealed class AppNetScope
{
    public string Adapter { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string Network { get; set; } = string.Empty;
    public string NetworkName { get; set; } = string.Empty;
    public List<AppNetRule> Apps { get; set; } = new();

    public AppNetScope Copy() => new() { Adapter = Adapter, AdapterName = AdapterName, Network = Network, NetworkName = NetworkName, Apps = Apps.Select(r => r.Copy()).ToList() };
}

/// A Network Traffic Control preset: the rules on every adapter and every adapter's and network's own.
public sealed class TrafficPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<AppNetRule> Apps { get; set; } = new();
    public List<AppNetScope> Scoped { get; set; } = new();
}

/// Network Traffic Control's settings (traffic.json): the rules, pins, the packet filter switch, its presets and the places made into rules.
public sealed class TrafficSettings
{
    /// The rules on every adapter (All adapters), and the adapters' and networks' own (<see cref="AppNetScope"/>).
    public List<AppNetRule> Apps { get; set; } = new();
    public List<AppNetScope> Scoped { get; set; } = new();

    /// The programs pinned to the top of the page, by path (or identity when the path was not known), in pin order.
    public List<string> Pinned { get; set; } = new();

    /// The "Packet filter" switch (<see cref="NetworkLimits.SetPacketFilter"/>): off keeps Windows Packet Filter out of all traffic. Not in
    /// presets: it is about this PC's drivers, not a setup.
    public bool PacketFilterOn { get; set; } = true;

    public List<TrafficPreset> Presets { get; set; } = new();

    /// The "Use <network>'s limits" actions whose place's own rules were made into an automation rule (once: a rule deleted in Automation stays
    /// deleted). Not in presets: rules are kept by Automation.
    public List<string> Rules { get; set; } = new();

    private static readonly DataStore Store = DataStores.Traffic;

    public static TrafficSettings Load() => AppDataStore.Load<TrafficSettings>(Store) ?? new();

    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// An import wrote traffic.json: read it again into this one instance and tell everyone. The preset list is refilled, never replaced.
    public bool Reload()
    {
        var fresh = Load();
        Presets.Clear();
        Presets.AddRange(fresh.Presets);
        fresh.Presets = Presets;
        SettingsCopy.Into(this, fresh);
        Commit();
        return true;
    }

    /// The rules changed (UI thread): the limits, the presets and the page follow.
    public event Action? Changed;

    /// Saves a change and tells everyone who follows it: the one way the rules are changed.
    public void Commit()
    {
        Save();
        Changed?.Invoke();
    }
}
