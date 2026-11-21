namespace OmniControl.App.Features.Volume.Pages;

/// The Volume presets' drop-down and cogwheel at the right of the tab row (both tabs), like the Mouse layers' on their group line: the
/// drop-down shows the preset in force and picking one applies it live; the cogwheel opens the preset manager at the Volume presets
/// (their group is registered by <see cref="VolumeControls"/>).
public sealed partial class VolumePageViewModel
{
    private readonly Action<string?, string?> _openPresets;

    /// The drop-down (its label is what search results show).
    public ChoiceItem PresetChoice => _presetChoice.Choice;

    private readonly ProviderChoice _presetChoice;

    /// The cogwheel: the preset manager at the Volume presets.
    public void ManagePresets() => _openPresets(Title, VolumeControls.PresetsId);
}
