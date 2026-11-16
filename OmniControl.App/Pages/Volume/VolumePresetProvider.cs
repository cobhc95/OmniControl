using OmniControl.App.Audio;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Volume presets (<see cref="VolumePresets"/>) as a preset group of the preset manager, like the mouse layers: the one in force
/// marked "in use" (it can't be deleted: edits are being saved into it), an empty one tagged so. Applying switches to the preset.
/// </summary>
internal sealed class VolumePresetProvider : IPresetProvider
{
    private const string EmptyTag = "empty";

    private readonly VolumePresets _presets;

    public VolumePresetProvider(VolumePresets presets) => _presets = presets;

    public PresetWords Words { get; } = new("Switch to", "Switch to Volume preset", "+ Add a preset",
        "Adds an empty preset; switching to it starts from the audio defaults");

    public PresetSwitch? Switch => null;

    public bool CanDuplicate => false;

    public bool CanExchangeFiles => false;

    public string? AddUnavailableReason => null;

    public IReadOnlyList<PresetEntry> Entries => _presets.Presets.Select(EntryOf).ToList();

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

    public PresetEntry? Find(string id) => _presets.Presets.FirstOrDefault(p => p.Id == id) is { } preset ? EntryOf(preset) : null;

    private string? NameOf(string id) => _presets.Presets.FirstOrDefault(p => p.Id == id)?.Name;

    public string? Apply(string id)
    {
        _presets.Apply(id);
        return null;
    }

    public PresetChange Add() => PresetChange.Made(_presets.Add());

    public PresetChange Rename(string id, string name) =>
        NameOf(id) is { } from && _presets.List.Rename(from, name) is { } problem ? PresetChange.Refused(problem) : PresetChange.Done;

    public PresetChange Move(string id, int index)
    {
        if (NameOf(id) is { } name) _presets.List.Move(name, index);
        return PresetChange.Done;
    }

    public Action? Delete(string id) => NameOf(id) is { } name && _presets.Presets.First(p => p.Id == id) != _presets.Active ? _presets.List.Delete(name) : null;

    public PresetChange Duplicate(string id) => PresetChange.Done;

    public byte[]? Export(string id) => null;

    public PresetChange Import(string fileName, byte[] bytes) => PresetChange.Done;

    public event Action? Changed
    {
        add => _presets.Changed += value;
        remove => _presets.Changed -= value;
    }
}
