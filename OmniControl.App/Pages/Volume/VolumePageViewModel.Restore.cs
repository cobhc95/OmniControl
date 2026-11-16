using OmniControl.App.Audio;
using OmniControl.App.Pages.Restore;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// "Restore defaults" on the Volume page when Advanced is shown: the same audio reset as the Easy page's button (<see cref="AudioDefaults"/>:
/// effects, combined devices, routes and methods removed, cables allowed; levels and mutes stay). Easy keeps its own button, line and
/// action (<c>volume.easy.restore</c>), so the shell adds no second action.
/// </summary>
public sealed partial class VolumePageViewModel
{
    private IRestorable? _restorable;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<MixerSettings>("volume", Title,
        () => AudioDefaults.Scope,
        () => _mixer.Settings, () => AudioDefaults.DefaultsOf(_mixer.Settings),
        target =>
        {
            AudioDefaults.Apply(new MixerDefaultsStore(_mixer), target);
            Easy.Refresh();
            return Task.FromResult(true);
        },
        hasOwnHotkey: true, atDefaults: AudioDefaults.IsAtDefaults);
}
