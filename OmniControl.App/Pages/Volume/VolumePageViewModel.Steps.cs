using OmniControl.App.Dialogs;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The install and remove steps at the foot of the master's Method menu (Equalizer APO's installer and Device Selector, OmniControl APO's advanced install, VB-CABLE), the master's and
/// microphones' Equalizer APO fixes, and the per-program Test.
/// </summary>
public sealed partial class VolumePageViewModel
{
    /// <summary>
    /// The install and remove steps at the foot of the master's Method menu: Equalizer APO's install (when it is missing), the virtual
    /// cable's (Install VB-CABLE, or More cables once one is installed), Choose devices (Equalizer APO's Device Selector), Remove
    /// VB-CABLE, then OmniControl APO's advanced install steps.
    /// </summary>
    private IReadOnlyList<CardAction> SetupSteps()
    {
        var apo = ApoActions();
        var steps = apo.Where(a => a == InstallApoAction).ToList();
        var choose = apo.Where(a => a.Command == ChooseDevicesCommand);
        if (_support.Current is { } support)
        {
            var installed = EffectsRules.CableInstalled(support);
            steps.Add(installed ? MoreCablesAction() : CableSetupAction());
            steps.AddRange(choose);
            if (installed) steps.Add(RemoveCableAction);
        }
        else steps.AddRange(choose);
        steps.AddRange(apo.Where(a => a != InstallApoAction && a.Command != ChooseDevicesCommand));
        return steps;
    }

    /// <summary>Remove VB-CABLE: its own setup (Remove Driver), or where to remove it.</summary>
    private CardAction RemoveCableAction => _removeCable ??= new CardAction("Remove VB-CABLE",
        "Opens VB-CABLE's own setup (Remove Driver) or tells where to remove it", new AsyncRelayCommand(() => RunSetupAsync(null, _installer.RemoveCableAsync())));

    private CardAction? _removeCable;

    /// <summary>
    /// The Equalizer APO steps: Choose devices whenever Equalizer APO is installed (its Device Selector registers it, turns the
    /// protected audio path off and puts it on playback devices and, on its Capture tab, microphones; the main button when the master
    /// can't run); Install (its own installer or page) when it is not installed or its config folder is gone. OmniControl APO's advanced
    /// install appears only when it was built and nothing is installed, and Remove only when it is installed.
    /// </summary>
    private IReadOnlyList<CardAction> ApoActions()
    {
        var actions = new List<CardAction>();
        var apo = _support.Current?.Apo ?? ApoState.None;
        var missing = _support.Current is not null && EffectsRules.ApoMissing(apo) is not null;
        if (EffectsRules.DeviceSelectorFixes(apo))
            actions.Add(new("Choose devices", "Opens Equalizer APO's Device Selector (administrator)",
                ChooseDevicesCommand, Main: missing));
        var none = !apo.UpstreamInstalled && !apo.AnyInstalled;
        if (missing && (none || apo.UpstreamInstalled && apo.UpstreamConfigFolder is null))
            actions.Add(InstallApoAction);
        if (none && EngineInstaller.ForkBuilt)
        {
            actions.Add(new("Advanced install", "Installs OmniControl APO (administrator)", new AsyncRelayCommand(InstallForkAsync)));
            actions.Add(new("Preview changes", "Lists the install's changes; changes nothing", new AsyncRelayCommand(PreviewForkAsync)));
        }
        if (apo.AnyInstalled)
            actions.Add(new("Remove OmniControl APO", "Removes OmniControl APO and restores the audio settings (administrator)", new AsyncRelayCommand(RemoveForkAsync)));
        return actions;
    }

    /// <summary>Equalizer APO's Device Selector (administrator): where it is put on playback devices and, on its Capture tab, on microphones.</summary>
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private CardAction? _speakerDevices;
    private CardAction? _cableDevices;
    private CardAction? _installApo;

    /// <summary>Install Equalizer APO: its own installer when it was placed next to the app, else its download page.</summary>
    private CardAction InstallApoAction => _installApo ??= new("Install Equalizer APO", EngineInstaller.EqualizerApoSetup is null
            ? "Opens Equalizer APO's download page"
            : "Runs Equalizer APO's own installer (administrator)",
        new AsyncRelayCommand(InstallEqualizerApoAsync), Main: true);

    /// <summary>
    /// The master's step when Equalizer APO can't run it on the speakers: Choose devices when its Device Selector fixes
    /// it (tick the speakers on its Playback tab), Install when it is not installed; null when nothing is missing or nothing here fixes it.
    /// </summary>
    private CardAction? MasterApoFix()
    {
        if (_engine.MasterFacts.ApoWhy is null) return null;
        var apo = _support.Current?.Apo ?? ApoState.None;
        if (EffectsRules.DeviceSelectorFixes(apo))
            return _speakerDevices ??= new CardAction("Choose devices",
                "Opens Equalizer APO's Device Selector: tick your speakers on its Playback tab (administrator). A restart may be needed.",
                ChooseDevicesCommand);
        return apo.UpstreamInstalled ? null : InstallApoAction;
    }

    /// <summary>The Test on a playing program: records it, silences the original each way in turn, measures the delay; always restores.</summary>
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

    /// <summary>
    /// Runs a setup step and shows how it ended in the main window's status line; a failure also opens a small message with the one
    /// next step, "Copy details" and where the log is.
    /// </summary>
    private async Task RunSetupAsync(string? progress, Task<SetupResult> work)
    {
        if (progress is not null) _report(progress);
        var result = await work;
        _report(result.Text);
        if (result.Failed)
            await _ask(new DialogMessage(SetupFailedTitle, $"{result.Text}\nDetails: {EngineInstaller.LogPath}", Details: result.Details));
    }

    private const string SetupFailedTitle = "The setup step didn't finish";

    /// <summary>Install for Equalizer APO: a short confirmation only when its installer will run (the download page needs none).</summary>
    /// <summary>The same step from outside the page (an FX window's device menu): one install path.</summary>
    public Task InstallEqualizerApoStepAsync() => InstallEqualizerApoAsync();

    private async Task InstallEqualizerApoAsync()
    {
        if (EngineInstaller.EqualizerApoSetup is not null
            && !await _ask(new DialogMessage("Install Equalizer APO?", EngineInstaller.EqualizerApoConfirm, "Install"))) return;
        await RunSetupAsync(null, _installer.InstallEqualizerApoAsync());
    }

    /// <summary>"Preview changes": what the advanced install would change; changes nothing.</summary>
    private async Task PreviewForkAsync()
    {
        var preview = await _installer.PreviewForkInstallAsync();
        await _ask(new DialogMessage("Preview: install OmniControl APO", "Nothing changed yet. The install would:",
            Details: preview.Text, ShowDetails: true));
    }

    /// <summary>The advanced install: a check first, then a short confirmation; only a clear yes starts the elevated setup.</summary>
    private async Task InstallForkAsync()
    {
        var preview = await _installer.PreviewForkInstallAsync();
        if (!preview.CanApply)
        {
            await _ask(new DialogMessage("OmniControl APO can't be installed here", preview.Summary, Details: preview.Text));
            return;
        }
        if (!await _ask(new DialogMessage("Install OmniControl APO?", EngineInstaller.ForkConfirm, "Install", Details: preview.Text))) return;
        await RunSetupAsync("Installing… sound pauses briefly", _installer.InstallForkAsync());
    }

    private async Task RemoveForkAsync()
    {
        var preview = await _installer.PreviewForkRemoveAsync();
        if (!preview.CanApply)
        {
            await _ask(new DialogMessage("Nothing to remove", preview.Summary, Details: preview.Text));
            return;
        }
        if (!await _ask(new DialogMessage("Remove OmniControl APO?", EngineInstaller.ForkRemoveConfirm, "Remove", Details: preview.Text))) return;
        await RunSetupAsync("Removing… sound pauses briefly", _installer.RemoveForkAsync());
    }
}
