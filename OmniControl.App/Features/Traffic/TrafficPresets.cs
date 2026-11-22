using System.Text.Json;
using OmniControl.App.Features.Resources;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Traffic;

/// Network traffic control's presets as a preset group: the built-in "Normal" (no limit or block anywhere) then the user's, each the rules on
/// every adapter and every adapter's and network's own; applying puts each back.
public sealed class TrafficPresets : ListPresetProvider<TrafficPreset>
{
    public const string GroupId = "traffic.presets", NormalName = "Normal";
    private const string NormalId = "traffic.normal";

    private readonly ResourcesSettings _settings;

    public TrafficPresets(ResourcesSettings settings) : base(new PresetList<TrafficPreset>(settings.TrafficPresets, settings.Save), builtIns: 1)
    {
        _settings = settings;
        settings.Changed += NotifyChanged; // the live rules may now be (or no longer be) a preset
    }

    public override PresetWords Words { get; } = new("Apply", "Apply Network traffic control preset", "+ Save the current rules as a preset",
        "Keeps every program's limits and blocks on All adapters and each adapter's and network's own");

    protected override IReadOnlyList<PresetEntry> MakeEntries()
    {
        var now = Text(Live());
        return List.Items.Select(p => new PresetEntry(p.Id, p.Name) { InUse = Text(p) == now })
            .Prepend(new PresetEntry(NormalId, NormalName) { InUse = Text(new TrafficPreset()) == now, BuiltIn = true, CanRename = false, CanDelete = false, CanMove = false })
            .ToList();
    }

    public override string? Apply(string id)
    {
        var preset = id == NormalId ? new TrafficPreset() : List.Items.FirstOrDefault(p => p.Id == id);
        if (preset is null) return "That preset is gone.";
        _settings.Setup.Apps = preset.Apps.Select(r => r.Copy()).ToList();
        _settings.Setup.Scoped = preset.Scoped.Select(s => s.Copy()).ToList();
        _settings.Commit();
        return null;
    }

    public override PresetChange Add()
    {
        var name = PresetRules.FirstFree("Traffic", List.Items.Select(p => p.Name).Append(NormalName).ToList());
        var preset = Live();
        preset.Name = name;
        List.Save(preset);
        return PresetChange.Made(preset.Id);
    }

    private TrafficPreset Live() => new() { Apps = _settings.Setup.Apps.Select(r => r.Copy()).ToList(), Scoped = _settings.Setup.Scoped.Select(s => s.Copy()).ToList() };

    private static string Text(TrafficPreset preset) => JsonSerializer.Serialize((preset.Apps, preset.Scoped), Json);

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
}
