using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Volume.Pages;

/// The Volume presets (<see cref="VolumePresets"/>) as a preset group of the preset manager, like the mouse layers: the one in force
/// marked "in use" (it can't be deleted: edits are being saved into it), an empty one tagged so. Applying switches to the preset.
internal sealed class VolumePresetProvider : PresetProvider
{
    private const string EmptyTag = "empty";

    private readonly VolumePresets _presets;

    public VolumePresetProvider(VolumePresets presets) => _presets = presets;

    public override PresetWords Words { get; } = new("Switch to", "Switch to Volume preset", "+ Add a preset",
        "Adds an empty preset; switching to it starts from the audio defaults");

    public override IReadOnlyList<PresetEntry> Entries => _presets.Presets.Select(EntryOf).ToList();

    private PresetEntry EntryOf(VolumePreset preset)
    {
        var inUse = preset == _presets.Active;
        return new PresetEntry(preset.Id, preset.Name)
        {
            InUse = inUse,
            CanDelete = !inUse,
            Tags = preset.Setup is null ? new[] { new TagItem(EmptyTag) } : Array.Empty<TagItem>(),
        };
    }

    public override PresetEntry? Find(string id) => _presets.Presets.FirstOrDefault(p => p.Id == id) is { } preset ? EntryOf(preset) : null;

    private string? NameOf(string id) => _presets.Presets.FirstOrDefault(p => p.Id == id)?.Name;

    public override string? Apply(string id)
    {
        _presets.Apply(id);
        return null;
    }

    public override PresetChange Add() => PresetChange.Made(_presets.Add());

    public override PresetChange Rename(string id, string name) =>
        NameOf(id) is { } from && _presets.List.Rename(from, name) is { } problem ? PresetChange.Refused(problem) : PresetChange.Done;

    public override PresetChange Move(string id, int index)
    {
        if (NameOf(id) is { } name) _presets.List.Move(name, index);
        return PresetChange.Done;
    }

    public override Action? Delete(string id) => NameOf(id) is { } name && _presets.Presets.First(p => p.Id == id) != _presets.Active ? _presets.List.Delete(name) : null;

    public override event Action? Changed
    {
        add => _presets.Changed += value;
        remove => _presets.Changed -= value;
    }
}
