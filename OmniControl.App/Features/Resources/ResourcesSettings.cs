using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources;

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
/// else its adapter's, else <see cref="ResourcesSetup.Apps"/>). The names are kept so an adapter or network that is gone still shows.
public sealed class AppNetScope
{
    public string Adapter { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string Network { get; set; } = string.Empty;
    public string NetworkName { get; set; } = string.Empty;
    public List<AppNetRule> Apps { get; set; } = new();

    public AppNetScope Copy() => new() { Adapter = Adapter, AdapterName = AdapterName, Network = Network, NetworkName = NetworkName, Apps = Apps.Select(r => r.Copy()).ToList() };
}

/// What a Resources preset holds: how often the page reads, the network adapter read, the taskbar gadget, the programs' network rules and pins.
public sealed class ResourcesSetup
{
    public double ReadEverySeconds { get; set; } = Timing.ResourcesReadEvery.TotalSeconds;

    /// The network adapter the Download and Upload readings come from: null = Auto (the busiest), <c>AdapterSpeed.All</c> = all together, else an adapter's id.
    public string? Adapter { get; set; }

    /// THE one choice of each temperature reading (<see cref="Reading.Sensor"/>), by reading id: the sensor it is read from (Windows' own, ids
    /// starting "win/", or the sensor library's). A reading not listed is on Auto; a sensor that is gone reads as Auto. Replaced whole on each
    /// change, so the poller's worker always reads one whole map.
    public SortedDictionary<string, string> Sensors { get; set; } = new();

    /// The Hottest sensor's, CPU and GPU temperature's choices in files from before <see cref="Sensors"/> (read only).
    [JsonPropertyName("Sensor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldHottestSensor { get => null; set { if (value is not null) SetSensor(Reading.HottestId, value); } }

    [JsonPropertyName("CpuSensor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldCpuSensor { get => null; set { if (value is not null) SetSensor(Reading.CpuTempId, value); } }

    [JsonPropertyName("GpuSensor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldGpuSensor { get => null; set { if (value is not null) SetSensor(Reading.GpuTempId, value); } }

    /// The sensor a temperature reading follows; null = Auto.
    public string? SensorOf(string readingId) => Sensors.GetValueOrDefault(readingId);

    public void SetSensor(string readingId, string? id)
    {
        var next = new SortedDictionary<string, string>(Sensors);
        if (id is null) next.Remove(readingId);
        else next[readingId] = id;
        Sensors = next;
    }

    /// The graphics adapter the GPU usage and GPU temperature readings follow (<see cref="Reading.HasGpuChoice"/>), by reading id: a
    /// <c>GraphicsAdapter.Id</c>; not listed = Auto, an adapter that is gone reads as Auto. Replaced whole on each change, as <see cref="Sensors"/>.
    public SortedDictionary<string, string> Gpus { get; set; } = new();

    /// The adapter a GPU reading follows; null = Auto.
    public string? GpuOf(string readingId) => Gpus.GetValueOrDefault(readingId);

    public void SetGpu(string readingId, string? id)
    {
        var next = new SortedDictionary<string, string>(Gpus);
        if (id is null) next.Remove(readingId);
        else next[readingId] = id;
        Gpus = next;
    }

    /// The network adapter the Download and Upload readings follow (<see cref="Reading.HasAdapterChoice"/>), by reading id: an
    /// <c>AdapterSpeed.Id</c>; not listed = Auto (the common <see cref="Adapter"/> choice), an adapter that is gone reads as Auto. Replaced whole on each change.
    public SortedDictionary<string, string> SpeedAdapters { get; set; } = new();

    /// The adapter a speed reading follows; null = Auto.
    public string? SpeedAdapterOf(string readingId) => SpeedAdapters.GetValueOrDefault(readingId);

    public void SetSpeedAdapter(string readingId, string? id)
    {
        var next = new SortedDictionary<string, string>(SpeedAdapters);
        if (id is null) next.Remove(readingId);
        else next[readingId] = id;
        SpeedAdapters = next;
    }

    public GadgetSettings Gadget { get; set; } = GadgetLayouts.NewLook();

    /// Network traffic control's rules on every adapter (All adapters), and the adapters' and networks' own (<see cref="AppNetScope"/>).
    public List<AppNetRule> Apps { get; set; } = new();
    public List<AppNetScope> Scoped { get; set; } = new();

    /// The programs pinned to the top of Network traffic control, by path (or identity when the path was not known), in pin order.
    public List<string> Pinned { get; set; } = new();

    public ResourcesSetup Copy() => JsonSerializer.Deserialize<ResourcesSetup>(JsonSerializer.Serialize(this))!;

    public bool Same(ResourcesSetup other) => JsonSerializer.Serialize(this) == JsonSerializer.Serialize(other);
}

public sealed class ResourcesPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public ResourcesSetup Setup { get; set; } = new();
}

/// A Network traffic control preset: the rules on every adapter and every adapter's and network's own.
public sealed class TrafficPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<AppNetRule> Apps { get; set; } = new();
    public List<AppNetScope> Scoped { get; set; } = new();
}

/// The user's own gadget layout (<see cref="Gadget.GadgetLayouts"/>): the gadget's look only.
public sealed class GadgetLayoutPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public GadgetSettings Look { get; set; } = new();

    /// When it was saved (the Layouts gallery's "Saved …"); null in a file from before it was kept.
    public DateTime? Saved { get; set; }
}

/// The Resources and Network traffic control pages' settings (resources.json): the live setup, the packet filter switch, the presets (Resources'
/// and Network traffic control's) and the gadget's own layouts.
public sealed class ResourcesSettings
{
    public ResourcesSetup Setup { get; set; } = new();

    /// The "Packet filter" switch (<see cref="Traffic.NetworkLimits.SetPacketFilter"/>): off keeps Windows Packet Filter out of all traffic. Not in
    /// presets: it is about this PC's drivers, not a setup.
    public bool PacketFilterOn { get; set; } = true;
    public List<ResourcesPreset> Presets { get; set; } = new();
    public List<GadgetLayoutPreset> Layouts { get; set; } = new();
    public List<TrafficPreset> TrafficPresets { get; set; } = new();

    /// The "Use <network>'s limits" actions whose place's own rules were made into an automation rule (once: a rule deleted in Automation stays
    /// deleted). Not in presets: rules are kept by Automation.
    public List<string> TrafficRules { get; set; } = new();

    private static readonly DataStore Store = DataStores.Resources;

    public static ResourcesSettings Load() => AppDataStore.Load<ResourcesSettings>(Store) ?? new();

    /// Written a moment later off the UI thread: typing a label or dragging a gap saves on every change.
    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// An import wrote resources.json: read it again into this one instance (both its modules hold it) and tell everyone. The preset lists
    /// keep the lists they were given: they are refilled, never replaced.
    public bool Reload()
    {
        var fresh = Load();
        Presets.Clear();
        Presets.AddRange(fresh.Presets);
        fresh.Presets = Presets;
        Layouts.Clear();
        Layouts.AddRange(fresh.Layouts);
        fresh.Layouts = Layouts;
        TrafficPresets.Clear();
        TrafficPresets.AddRange(fresh.TrafficPresets);
        fresh.TrafficPresets = TrafficPresets;
        SettingsCopy.Into(this, fresh);
        Commit();
        return true;
    }

    /// The setup changed (UI thread): the gadget, the limits, the presets and the page follow.
    public event Action? Changed;

    /// Saves a change of the setup and tells everyone who follows it: the one way the setup is changed.
    public void Commit()
    {
        Setup.Gadget.Complete();
        Save();
        Changed?.Invoke();
    }

    private bool _commitPosted;

    /// <see cref="Commit"/> once a burst of changes is over (clicks through the label editor's icons, typing, a dragged colour; UI thread): each
    /// commit redraws the gadget and the whole tab, so a queue of them would pile up behind each other.
    public void CommitSoon()
    {
        if (_commitPosted) return;
        _commitPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _commitPosted = false;
            Guard.Run("Resources: save a change", Commit);
        }, DispatcherPriority.Background);
    }
}
