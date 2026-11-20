using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages;

/// The install and remove steps at the foot of the master's Method menu (Equalizer APO's download page and Device Selector, VB-CABLE), the master's and
/// microphones' Equalizer APO fixes, and the per-program Test.
public sealed partial class VolumePageViewModel
{
    /// The install and remove steps at the foot of the master's Method menu: Equalizer APO's install (when it is missing), the virtual
    /// cable's (Install VB-CABLE, or More cables once one is installed), Choose devices (Equalizer APO's Configurator, or for OmniControl's own copy its device window), Remove VB-CABLE.
    private IReadOnlyList<CardAction> SetupSteps()
    {
        var apo = ApoActions();
        var steps = apo.Where(a => a == InstallApoAction).ToList();
        var choose = apo.Where(IsChoose);
        if (_support.Current is { } support)
        {
            var installed = EffectsRules.CableInstalled(support);
            steps.Add(installed ? MoreCablesAction() : CableSetupAction());
            steps.AddRange(choose);
            if (installed) steps.Add(RemoveCableAction);
        }
        else steps.AddRange(choose);
        steps.AddRange(apo.Where(a => a != InstallApoAction && !IsChoose(a)));
        return steps;
    }

    /// Remove VB-CABLE: where to remove it.
    private CardAction RemoveCableAction => _removeCable ??= new CardAction("Remove VB-CABLE",
        "Tells where to remove VB-CABLE and opens VB-Audio's page",new AsyncRelayCommand(() => RunSetupAsync(null, _installer.RemoveCableAsync())));

    private CardAction? _removeCable;

    /// The Equalizer APO steps: Choose devices whenever Equalizer APO is installed separately (its Device Selector registers it, turns the
    /// protected audio path off and puts it on playback devices and, on its Capture tab, microphones; the main button when the master
    /// can't run); Install (OmniControl's own copy, or the download page) when it is not installed, its config folder is gone, or
    /// OmniControl's copy is not on the speakers.
    private IReadOnlyList<CardAction> ApoActions()
    {
        var actions = new List<CardAction>();
        var apo = _support.Current?.Apo ?? ApoState.None;
        var missing = _support.Current is not null && EffectsRules.ApoMissing(apo) is not null;
        if (EffectsRules.DeviceSelectorFixes(apo))
            actions.Add(new("Choose devices", "Opens Equalizer APO's Configurator (administrator)",
                ChooseDevicesCommand, Main: missing));
        else if (apo.Ownership.Owner == ApoOwner.Ours)
            actions.Add(new("Choose devices", "Chooses the devices OmniControl's Equalizer APO runs on",
                _openApoDevices ??= new RelayCommand(Apo.OpenDevices)));
        if (missing && (!apo.UpstreamInstalled || apo.UpstreamConfigFolder is null || apo.Ownership.Owner == ApoOwner.Ours))
            actions.Add(InstallApoAction);
        return actions;
    }

    /// Equalizer APO's Configurator (administrator): where it is put on playback devices and, on its Capture tab, on microphones.
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private IRelayCommand? _openApoDevices;

    private bool IsChoose(CardAction a) => a.Command == ChooseDevicesCommand || a.Command == _openApoDevices;
    private CardAction? _speakerDevices;
    private CardAction? _cableDevices;
    private IAsyncRelayCommand? _installApo;

    /// Install Equalizer APO: the one shared step (<see cref="EngineInstaller.SetUpEqualizerApoAsync"/>), its tooltip saying what it opens now.
    private CardAction InstallApoAction => new(_installer.SetUpLabel, _installer.SetUpTip,
        _installApo ??= new AsyncRelayCommand(InstallEqualizerApoAsync), Main: true);

    /// The master's step when Equalizer APO can't run it on the speakers: Choose devices when a separate install's Device Selector fixes
    /// it (tick the speakers on its Playback tab), Install when it is not installed or OmniControl's copy is not on them; null when nothing
    /// is missing or nothing here fixes it.
    private CardAction? MasterApoFix()
    {
        var apo = _support.Current?.Apo ?? ApoState.None;
        var bundled = EffectsRules.BundledFixes(apo, apo.PlaybackOf(_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId));
        if (_engine.MasterFacts.ApoWhy is null && !bundled) return null;
        if (EffectsRules.DeviceSelectorFixes(apo))
            return _speakerDevices ??= new CardAction("Choose devices",
                "Opens Equalizer APO's Configurator: tick your speakers on its Playback tab (administrator). A restart may be needed.",
                ChooseDevicesCommand);
        return bundled || !apo.UpstreamInstalled ? InstallApoAction : null;
    }

    /// The Test on a playing program: records it, silences the original each way in turn, measures the delay; always restores.
    private async Task TestCaptureAsync(VolumeRow row)
    {
        var running = EffectsRules.TestRunning();
        row.TestNote = running.Brief;
        row.TestTip = running.Full;
        var result = await _engine.TestAsync(row.Key);
        row.TestNote = result.Brief;
        row.TestTip = result.Full;
        _report(StatusMessage.Compose(result.Brief, result.Full));
    }

    /// Runs a setup step and shows how it ended in the main window's status line; a failure also opens a small message with the one
    /// next step, "Copy details" and where the log is.
    private async Task RunSetupAsync(string? progress, Task<SetupResult> work)
    {
        if (progress is not null) _report(progress);
        var result = await work;
        _report(result.Text);
        if (result.Failed)
            await _ask(new DialogMessage(SetupFailedTitle, $"{result.Text}\nDetails: {EngineInstaller.LogPath}", Details: result.Details));
    }

    private const string SetupFailedTitle = "The setup step didn't finish";

    /// The same step from outside the page (an FX window's device menu): one install path.
    public Task InstallEqualizerApoStepAsync() => InstallEqualizerApoAsync();

    /// Install for Equalizer APO (<see cref="EngineInstaller.SetUpEqualizerApoAsync"/>: its Configurator when it is installed, OmniControl's
    /// own copy after asking, else its download page).
    private async Task InstallEqualizerApoAsync()
    {
        var question = _installer.SetUpQuestion();
        if (question is not null && !await _ask(question)) return;
        await RunSetupAsync(question is null ? null : "Setting up Equalizer APO; Windows audio restarts for a moment…", _installer.SetUpEqualizerApoAsync());
    }

}
