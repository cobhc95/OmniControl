using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Volume.Pages;

/// The Volume presets' drop-down and cogwheel at the right of the tab row (both tabs), like the Mouse layers' on their group line: the
/// drop-down shows the preset in force and picking one applies it live; the cogwheel opens the preset manager at the Volume presets.
public sealed partial class VolumePageViewModel
{
    /// The preset group's id (part of its presets' action ids: never rename).
    public const string PresetsId = "volume.presets";

    private const string PresetsGroupName = "Volume presets";

    private readonly VolumePresets _presets;
    private readonly Action<string?, string?> _openPresets;

    /// The drop-down (its label is what search results show).
    public ChoiceItem PresetChoice => _presetChoice!.Choice;

    private ProviderChoice? _presetChoice;

    /// Registers the presets as a preset group (the manager and the presets' shortcuts follow from there).
    private void RegisterPresets(IPresetGroups groups)
    {
        var provider = new VolumePresetProvider(_presets);
        _presetChoice = new ProviderChoice(PresetsGroupName, provider, ManagePresets);
        groups.Register(new PresetGroup(PresetsId, Title, PresetsGroupName, provider));
    }

    /// The cogwheel: the preset manager at the Volume presets.
    public void ManagePresets() => _openPresets(Title, PresetsId);
}
