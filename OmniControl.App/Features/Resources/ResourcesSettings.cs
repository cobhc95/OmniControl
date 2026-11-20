using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources;

/// One program's network rule: an upload and a download limit in KB/s (kept while switched off, so the chip can turn them on again) and the block.
public sealed class AppNetRule
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int UploadKbps { get; set; }
    public bool UploadOn { get; set; }
    public int DownloadKbps { get; set; }
    public bool DownloadOn { get; set; }
    public bool Blocked { get; set; }

    /// True when the rule asks nothing of Windows (it may be forgotten once Windows was told).
    [JsonIgnore] public bool IsPlain => !UploadOn && !DownloadOn && !Blocked;
}

/// What a Resources preset holds: how often the page reads, the network adapter read, the taskbar gadget, the programs' network rules and pins.
public sealed class ResourcesSetup
{
    public double ReadEverySeconds { get; set; } = Timing.ResourcesReadEvery.TotalSeconds;

    /// The network adapter the Download and Upload readings come from: null = Auto (the busiest), <c>AdapterSpeed.All</c> = all together, else an adapter's id.
    public string? Adapter { get; set; }

    /// The temperature sensor the Hottest sensor reading follows (a sensor's id from the sensor library): null = the hottest of all (Auto); one that is gone reads as Auto.
    public string? Sensor { get; set; }
    public GadgetSettings Gadget { get; set; } = GadgetLayouts.NewLook();
    public List<AppNetRule> Apps { get; set; } = new();

    /// The programs pinned to the top of Network by app, by path (or identity when the path was not known), in pin order.
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

/// The user's own gadget layout (<see cref="Gadget.GadgetLayouts"/>): the gadget's look only.
public sealed class GadgetLayoutPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public GadgetSettings Look { get; set; } = new();

    /// When it was saved (the Layouts gallery's "Saved …"); null in a file from before it was kept.
    public DateTime? Saved { get; set; }
}

/// The Resources page's settings (resources.json): the live setup, the Network by app group's Apps scope, the presets and the gadget's own layouts.
public sealed class ResourcesSettings
{
    public ResourcesSetup Setup { get; set; } = new();
    public AppScopeSetting? Scope { get; set; }
    public List<ResourcesPreset> Presets { get; set; } = new();
    public List<GadgetLayoutPreset> Layouts { get; set; } = new();

    private static readonly DataStore Store = DataStores.Resources;

    public static ResourcesSettings Load() => AppDataStore.Load<ResourcesSettings>(Store) ?? new();

    /// Written a moment later off the UI thread: typing a label or dragging a gap saves on every change.
    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// The setup changed (UI thread): the gadget, the limits, the presets and the page follow.
    public event Action? Changed;

    /// Saves a change of the setup and tells everyone who follows it: the one way the setup is changed.
    public void Commit()
    {
        Setup.Gadget.Complete();
        Save();
        Changed?.Invoke();
    }
}
