namespace OmniControl.App.Shared.Presets;

/// What a feature with presets needs of the master list of preset groups: it registers its own (<see cref="PresetGroups"/> is the one).
public interface IPresetGroups
{
    void Register(PresetGroup group);

    void Register(IEnumerable<PresetGroup> groups);
}
