using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The global sound mode: storing a pick, the panel's cards with each mode's status on this PC, the install and fix buttons that used
/// to sit on the per-row mode cards (Equalizer APO's installer and Device Selector, OmniControl APO's advanced install, VB-CABLE), the
/// search card and Fast mode's per-program Test.
/// </summary>
public sealed partial class VolumePageViewModel
{
    private void SetSoundMode(SoundMode mode)
    {
        _mixer.SetSoundMode(mode);
        SyncModes();
    }

    private void SyncSoundMode()
    {
        SoundMode.Sync(_mixer.SoundMode, _modes.StatusOf, ActionsFor);
        if (_soundModeCard.Index != IndexOf(_mixer.SoundMode)) _soundModeCard.Index = IndexOf(_mixer.SoundMode);
        _soundModeCard.Refresh();
    }

    /// <summary>The search card's preview: the current mode's first status line.</summary>
    private Control SoundModePreview() => new TextBlock
    {
        Text = _modes.StatusOf(_mixer.SoundMode).FirstOrDefault()?.Text ?? _mixer.SoundMode.Description,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Classes = { "hint", "small" },
    };

    /// <summary>The buttons a mode's card offers now.</summary>
    private IReadOnlyList<ModeCardAction> ActionsFor(SoundMode mode) => mode.Setup switch
    {
        ModeSetup.EqualizerApo => ApoActions(),
        _ => CableActions(),
    };

    private IReadOnlyList<ModeCardAction> CableActions()
    {
        if (_modes.Current is not { } support) return Array.Empty<ModeCardAction>();
        return support.Cables.Count == 0
            ? new[] { CableSetupAction() }
            : new[]
            {
                MoreCablesAction(),
                new ModeCardAction("Remove VB-CABLE", "Opens VB-CABLE's own setup (Remove Driver) or tells where to remove it",
                    new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Full, null, _installer.RemoveCableAsync()))),
            };
    }

    /// <summary>
    /// Fast mode's Equalizer APO buttons: Choose devices whenever Equalizer APO is installed (its Device Selector registers it, turns the
    /// protected audio path off and puts it on playback devices and, on its Capture tab, microphones; the main button when the master
    /// can't run); Install (its own installer or page) when it is not installed or its config folder is gone. OmniControl APO's advanced
    /// install appears only when it was built and nothing is installed, and Remove only when it is installed.
    /// </summary>
    private IReadOnlyList<ModeCardAction> ApoActions()
    {
        var actions = new List<ModeCardAction>();
        var apo = _modes.Current?.Apo ?? ApoState.None;
        var missing = _modes.Current is not null && SoundModes.ApoMissing(apo) is not null;
        if (SoundModes.DeviceSelectorFixes(apo))
            actions.Add(new("Choose devices", "Opens Equalizer APO's Device Selector: tick the playback device, and microphones on its Capture tab (administrator)",
                ChooseDevicesCommand, Main: missing));
        var none = !apo.UpstreamInstalled && !apo.AnyInstalled;
        if (missing && (none || apo.UpstreamInstalled && apo.UpstreamConfigFolder is null))
            actions.Add(new("Install Equalizer APO", EngineInstaller.EqualizerApoSetup is null
                    ? "Opens Equalizer APO's download page"
                    : "Runs Equalizer APO's own installer (administrator)",
                new AsyncRelayCommand(InstallEqualizerApoAsync), Main: true));
        if (none && EngineInstaller.ForkBuilt)
        {
            actions.Add(new("Advanced install", "Installs OmniControl APO, OmniControl's own build of Equalizer APO (administrator)", new AsyncRelayCommand(InstallForkAsync)));
            actions.Add(new("Preview changes", "Lists every change the advanced install would make; changes nothing", new AsyncRelayCommand(PreviewForkAsync)));
        }
        if (apo.AnyInstalled)
            actions.Add(new("Remove", "Removes OmniControl APO and puts the original audio settings back (administrator)", new AsyncRelayCommand(RemoveForkAsync)));
        return actions;
    }

    /// <summary>Equalizer APO's Device Selector (administrator): where it is put on playback devices and, on its Capture tab, on microphones.</summary>
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Fast, null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private ModeCardAction? _micDevices;

    /// <summary>
    /// A microphone row's step when Equalizer APO can't run its effects in Fast mode: Choose devices when its Device Selector fixes it
    /// (not on this microphone, not registered, enhancements off), else nothing (Install is on the Fast mode card).
    /// </summary>
    private ModeCardAction? MicApoFix(string endpointId)
    {
        var apo = _modes.Current?.Apo ?? ApoState.None;
        if (SoundModes.MicApoMissing(apo, endpointId) is null || !SoundModes.DeviceSelectorFixes(apo)) return null;
        return _micDevices ??= new ModeCardAction("Choose devices",
            "Opens Equalizer APO's Device Selector: tick this microphone on its Capture tab, then OK (administrator). A restart may be needed. Your own Equalizer APO settings also apply to it unless a Device line limits them.",
            ChooseDevicesCommand);
    }

    /// <summary>Fast mode's Test on a playing program: records it, mutes the original both ways, measures the delay; always restores.</summary>
    private async Task TestCaptureAsync(VolumeRow row)
    {
        row.TestNote = "Testing… keep the program playing";
        var result = await _engine.TestAsync(row.Key);
        row.TestNote = result;
        _report(result);
    }

    /// <summary>
    /// Runs a setup step and shows how it ended on the mode's card and in the footer; a failure also opens a small message with the one
    /// next step, "Copy details" and where the log is.
    /// </summary>
    private async Task RunSetupAsync(SoundMode mode, string? progress, Task<SetupResult> work)
    {
        if (progress is not null) SoundMode.SetNote(mode, progress);
        var result = await work;
        SoundMode.SetNote(mode, result.Text);
        _report(result.Text);
        if (result.Failed)
            await _ask(new EngineMessage($"{mode.Name}: setup didn't finish", $"{result.Text}\nDetails: {EngineInstaller.LogPath}", Details: result.Details));
    }

    /// <summary>Install for Equalizer APO: a short confirmation only when its installer will run (the download page needs none).</summary>
    private async Task InstallEqualizerApoAsync()
    {
        if (EngineInstaller.EqualizerApoSetup is not null
            && !await _ask(new EngineMessage("Install Equalizer APO?", EngineInstaller.EqualizerApoConfirm, "Install"))) return;
        await RunSetupAsync(SoundModes.Fast, null, _installer.InstallEqualizerApoAsync());
    }

    /// <summary>"Preview changes": what the advanced install would change; changes nothing.</summary>
    private async Task PreviewForkAsync()
    {
        var preview = await _installer.PreviewForkInstallAsync();
        await _ask(new EngineMessage("Preview: install OmniControl APO", "Nothing has been changed. Install would make these changes:",
            Details: preview.Text, ShowDetails: true));
    }

    /// <summary>The advanced install: a check first, then a short confirmation; only a clear yes starts the elevated setup.</summary>
    private async Task InstallForkAsync()
    {
        var preview = await _installer.PreviewForkInstallAsync();
        if (!preview.CanApply)
        {
            await _ask(new EngineMessage("OmniControl APO can't be installed here", preview.Summary, Details: preview.Text));
            return;
        }
        if (!await _ask(new EngineMessage("Install OmniControl APO?", EngineInstaller.ForkConfirm, "Install", Details: preview.Text))) return;
        await RunSetupAsync(SoundModes.Fast, "Installing… sound pauses for a few seconds", _installer.InstallForkAsync());
    }

    private async Task RemoveForkAsync()
    {
        var preview = await _installer.PreviewForkRemoveAsync();
        if (!preview.CanApply)
        {
            await _ask(new EngineMessage("Nothing to remove", preview.Summary, Details: preview.Text));
            return;
        }
        if (!await _ask(new EngineMessage("Remove OmniControl APO?", EngineInstaller.ForkRemoveConfirm, "Remove", Details: preview.Text))) return;
        await RunSetupAsync(SoundModes.Fast, "Removing… sound pauses for a few seconds", _installer.RemoveForkAsync());
    }

    /// <summary>Hotkey: the next sound mode, wrapping round; the footer says which and what it still needs.</summary>
    private void NextSoundMode()
    {
        var next = SoundModes.Next(_mixer.SoundMode);
        SetSoundMode(next);
        var caution = _modes.StatusOf(next).FirstOrDefault(s => s.IsCaution);
        _report(caution is null ? $"Sound mode: {next.Name}" : $"Sound mode: {next.Name} ({caution.Text})");
    }
}
