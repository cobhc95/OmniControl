using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages;

/// The steps at the foot of the master's Method menu ("Equalizer APO devices…", VB-CABLE), the master's and a cable's Equalizer APO
/// fixes, and the per-program Test.
public sealed partial class VolumePageViewModel
{
    /// The steps at the foot of the master's Method menu: "Equalizer APO devices…" (installing and uninstalling Equalizer APO happens only
    /// there), then the virtual cable's (Install VB-CABLE, or More cables and Remove VB-CABLE once one is installed).
    private IReadOnlyList<CardAction> SetupSteps()
    {
        var steps = new List<CardAction> { Apo.Devices };
        if (_support.Current is not { } support) return steps;
        var installed = EffectsRules.CableInstalled(support);
        steps.Add(installed ? MoreCablesAction() : CableSetupAction());
        if (installed) steps.Add(RemoveCableAction);
        return steps;
    }

    /// Remove VB-CABLE: where to remove it.
    private CardAction RemoveCableAction => _removeCable ??= new CardAction("Remove VB-CABLE",
        "Tells where to remove VB-CABLE and opens VB-Audio's page",new AsyncRelayCommand(() => RunSetupAsync(null, _installer.RemoveCableAsync())));

    private CardAction? _removeCable;

    /// A separate Equalizer APO's Configurator (administrator): where it is put on a cable (a program's Method menu, Easy).
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private CardAction? _cableDevices;

    /// The master's step when Equalizer APO does not run on its speakers: "Equalizer APO devices…"; null when it runs there.
    private CardAction? MasterApoFix() =>
        _engine.MasterFacts.ApoWhy is null && _engine.MasterFacts.ApoStand == ApoStand.Running ? null : Apo.Devices;

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

}
