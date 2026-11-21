using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages;

/// The install row of the master's and a microphone's Method menu, the master's and a cable's Equalizer APO fixes, and the per-program Test.
public sealed partial class VolumePageViewModel
{
    /// The install row at the foot of a Method menu: Install Equalizer APO… and Install VB-CABLE…, each only while its program is missing
    /// (none left = no row). The menu's fix step above the cards is never the installer (<see cref="MenuFixOf"/>).
    private IReadOnlyList<CardAction> SetupSteps()
    {
        var steps = new List<CardAction>();
        if (!Apo.Installed) steps.Add(Apo.Devices with { Main = true });
        if (_support.Current is { } support && !EffectsRules.CableInstalled(support)) steps.Add(CableSetupAction());
        return steps;
    }

    /// The fix step a Method menu shows above its cards: only Equalizer APO's Device Selector; its installer sits in <see cref="SetupSteps"/>.
    private CardAction? MenuFixOf(CardAction? fix) => Apo.Installed ? fix : null;

    /// Equalizer APO's Device Selector (administrator): where it is put on a cable (a program's Method menu, Easy).
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private CardAction? _cableDevices;

    /// The master's or a microphone's step when Equalizer APO does not run on its device (<see cref="EqualizerApoGroup.Devices"/>); null when it runs there.
    private CardAction? ApoFixFor(MasterFacts facts) => facts.ApoWhy is null && facts.ApoStand == ApoStand.Running ? null : Apo.Devices;

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
        await EngineInstaller.ShowAsync(await work, _report, _ask);
    }
}
