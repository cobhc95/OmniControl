using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Volume.Pages.Easy;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.App.Features.Volume.Pages;

/// The page's two views: Easy (<see cref="EasyViewModel"/>, new) and Advanced (everything else in this class, unchanged). Only the
/// choice lives here: it is kept in the app settings, so the page opens in the view the user left; the first start decides it
/// (<see cref="FirstRun"/>). Easy reads and writes through the Advanced rows and the same services, never through copies.
public sealed partial class VolumePageViewModel
{
    private bool _isEasy;

    /// The Easy view; made once the Advanced rows exist, since its rows wrap them.
    public EasyViewModel Easy { get; private set; } = null!;

    public override bool ShowsOwnHeader => true; // the Easy | Advanced switch sits beside the title

    public bool IsEasy
    {
        get => _isEasy;
        private set
        {
            if (!SetProperty(ref _isEasy, value)) return;
            _settings.View.Value = value ? VolumeView.Easy : VolumeView.Advanced;
            OnPropertyChanged(nameof(IsAdvanced));
            if (value) Easy.Shown();
        }
    }

    public bool IsAdvanced => !IsEasy;

    [RelayCommand] private void ShowEasy() => IsEasy = true;
    [RelayCommand] private void ShowAdvanced() => IsEasy = false;

    /// What Easy's guided setups use of this page: the cable rules and the setup steps (Equalizer APO's installer and Device Selector,
    /// VB-CABLE) are the ones Advanced's buttons run, handed in as they are, so both views say and do the same.
    private EasyFlowServices NewEasyFlowServices()
    {
        var environment = new EasyEnvironment(_mixer, _support, _engine, FreeCableFor, microphones => microphones ? BusHeldCable() : BusCableName());
        var steps = new EasySetupSteps(Apo.DevicesAsync, () => ChooseDevicesCommand.ExecuteAsync(null),
            () => RunSetupAsync(null, _installer.InstallCableAsync()), () => _report(EngineInstaller.OpenCablePage().Text));
        return new EasyFlowServices(environment.Read, new EasyActions(new MixerEasyStore(_mixer), steps),
            question => _ask(new DialogMessage(question.Title, question.Text, question.Button)));
    }

    private void CreateEasy(IActionRegistry hotkeys)
    {
        Easy = new EasyViewModel(_mixer, _support, _engine, _master, Outputs, Inputs, _openFx, NewEasyFlowServices(), _settings.EasyWelcomeDone);
        hotkeys.Register(new HotkeyAction("volume.view.toggle", "Switch Volume view (Easy / Advanced)", Title, "sound audio volume easy advanced simple view mode switch",
            _ => OnUi(() => IsEasy = !IsEasy)));
        hotkeys.Register(new HotkeyAction("volume.easy.sound.next", "Next sound preset for everything", Title,
            "sound audio master easy preset bass boost vocal night loud equaliser eq next",
            devices => OnUi(() => { _ = Easy.NextMasterPresetAsync(); })));
        hotkeys.Register(new HotkeyAction("volume.easy.restore", "Restore audio defaults", Title,
            "reset default audio restore sound effects off undo easy",
            devices => OnUi(() => { _ = Easy.RestoreDefaultsAsync(); })));
    }
}
