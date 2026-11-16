using OmniControl.App.Audio;
using OmniControl.App.Presets;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Volume presets' drop-down and cogwheel at the right of the tab row (both tabs), like the Mouse layers' on their group line: the
/// drop-down shows the preset in force and picking one applies it live; the cogwheel opens the preset manager at the Volume presets.
/// </summary>
public sealed partial class VolumePageViewModel
{
    /// <summary>The preset group's id (part of its presets' action ids: never rename).</summary>
    public const string PresetsId = "volume.presets";

    private const string PresetsGroupName = "Volume presets";

    private readonly VolumePresets _presets;
    private readonly Action<string?, string?> _openPresets;

    /// <summary>The drop-down (its label is what search results show).</summary>
    public ChoiceItem PresetChoice => _presetChoice!.Choice;

    private ProviderChoice? _presetChoice;

    /// <summary>Registers the presets as a preset group (the manager and the presets' shortcuts follow from there).</summary>
    private void RegisterPresets(PresetGroups groups)
    {
        var provider = new VolumePresetProvider(_presets);
        _presetChoice = new ProviderChoice(PresetsGroupName, provider, ManagePresets);
        groups.Register(new PresetGroup(PresetsId, Title, PresetsGroupName, provider));
    }

    /// <summary>The cogwheel: the preset manager at the Volume presets.</summary>
    public void ManagePresets() => _openPresets(Title, PresetsId);
}
