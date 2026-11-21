using OmniControl.App.Features.States;
using OmniControl.App.Features.Tray;
using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The tray quick panel with the default tiles and sliders, filled from the master lists. Its lists are read once (Showing) and the panel
    /// is told it is hidden again at once, which cancels the background re-reads of smart devices it starts (the sample devices also have
    /// "check when the tray opens" off), so no device is asked.
    private static void TrayPanel(Rig rig, Camera camera)
    {
        var model = Tray(rig);
        model.Showing();
        model.Hidden();
        camera.Window(new TrayFlyoutWindow { DataContext = model }, "tray-panel");
    }

    /// Stand-ins for the Volume feature's own states (same ids, names and kinds as VolumeStates), with sample values: that feature's
    /// states come from the mixer, which reads and writes the real sound devices, so it is not made here.
    private static void SampleVolumeStates(Rig rig)
    {
        const string title = "Volume", how = "Sample value (screenshot tool).";
        var speakers = new[] { "Speakers", "Headphones" };
        rig.States.Register(new[]
        {
            new StateEntry(VolumeStates.MasterVolumeId, "Master volume", title, StateKind.Level, () => StateValue.Of(42, DateTime.Now), how,
                Unit: "%", Set: _ => { }, Short: "Volume", SuggestedIcons: new[] { "volume", "volume-low", "speaker" }, Common: CommonGroups.Sound),
            new StateEntry(VolumeStates.MasterFxId, "Master effects on", title, StateKind.OnOff, () => StateValue.Of(true, DateTime.Now), how,
                Set: _ => { }, Short: "FX", Icon: App.Features.Icons.IconLibrary.Sliders),
            new StateEntry(VolumeStates.DefaultSpeakerId, "Default speaker", title, StateKind.Choice, () => StateValue.Of(speakers[0], DateTime.Now), how,
                Choices: () => speakers, Set: _ => { }, Common: CommonGroups.Sound),
            new StateEntry(VolumeStates.AllSpeakersId, "All speakers volume", title, StateKind.Level, () => StateValue.Of(42, DateTime.Now), how,
                Unit: "%", Set: _ => { }, Short: "All speakers", SuggestedIcons: new[] { "volume", "speaker" }),
            new StateEntry(VolumeStates.MicMutedId, "Mic muted", title, StateKind.OnOff, () => StateValue.Of(false, DateTime.Now), how,
                Set: _ => { }, Short: "Mic mute", Compact: "Mic", StateIcons: new OnOffIcons("mic-off", App.Features.Icons.IconLibrary.Mic), Icon: App.Features.Icons.IconLibrary.Mic,
                Common: CommonGroups.Sound),
        });
    }
}
