using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The global sound mode and the numbered setups: storing a pick (Full control restores its remembered setup, leaving it remembers the
/// live one), the setup table and the badge, the panel's cards with each mode's status on this PC, the install and fix buttons that
/// used to sit on the per-row mode cards (Equalizer APO's installer and Device Selector, OmniControl APO's advanced install, VB-CABLE),
/// the master's Equalizer APO fix, the search card and Fast mode's per-program Test.
/// </summary>
public sealed partial class VolumePageViewModel
{
    /// <summary>
    /// A mode was picked (the panel, the search card, the hotkey, a Switch link). Choosing Full control restores the remembered Full
    /// control setup, or setup 1 with one line saying why when it can't run here; leaving it remembers the live setup.
    /// </summary>
    private void SetSoundMode(SoundMode mode)
    {
        if (ModeWhy(mode) is { } why)
        {
            _report(why); // a mode that can't work here is never switched to (its card, the search card, the hotkey and every Switch link agree)
            SyncSoundMode();
            return;
        }
        var from = _mixer.SoundMode;
        if (mode == SoundModes.Full && from != SoundModes.Full)
        {
            var (number, note) = SoundModes.RestoreSetup(_mixer.FullSetup, _engine.Help);
            _mixer.ApplySetup(number, FoundCables);
            if (note is not null) _report(note);
        }
        else if (mode != SoundModes.Full && from == SoundModes.Full)
        {
            _mixer.RememberFullSetup(SoundModes.SetupToRemember(SoundModes.SetupNumber(_engine.Help), _mixer.CablesKeptFree));
            _mixer.ApplySetup(SoundModes.FastSetup, FoundCables);
        }
        else _mixer.SetSoundMode(mode);
        SyncModes();
    }

    /// <summary>A row of the setup table: the setup is applied in one step (Fast mode through the mode switch, so the Full control setup is remembered).</summary>
    private void ChooseSetup(int number)
    {
        if (number == SoundModes.FastSetup)
        {
            SetSoundMode(SoundModes.Fast);
            return;
        }
        _mixer.ApplySetup(number, FoundCables);
        SyncModes();
    }

    private IReadOnlyList<Core.Engine.FoundCable> FoundCables => _modes.Current?.Found ?? Array.Empty<Core.Engine.FoundCable>();

    /// <summary>The setup table, kept in step with the engine.</summary>
    private void ShowSetupTable()
    {
        _setupTable.Sync(_engine.Help);
        _showTable(_setupTable);
    }

    private void SyncSoundMode()
    {
        SoundMode.Sync(_mixer.SoundMode, _modes.StatusOf, ActionsFor, ModeWhy);
        SyncSetup();
        if (_soundModeCard.Index != IndexOf(_mixer.SoundMode)) _soundModeCard.Index = IndexOf(_mixer.SoundMode);
        _soundModeCard.Refresh();
    }

    /// <summary>The badge and the table follow the live setup (<see cref="SoundModes.SetupNumber"/>).</summary>
    private void SyncSetup()
    {
        SoundMode.SyncSetup(SoundModes.SetupNumber(_engine.Help));
        _setupTable.Sync(_engine.Help);
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

    /// <summary>Full control's card: install or more cables, remove, and Help (the setup table).</summary>
    private IReadOnlyList<ModeCardAction> CableActions()
    {
        if (_modes.Current is not { } support) return new[] { HelpAction };
        return !SoundModes.CableInstalled(support)
            ? new[] { CableSetupAction(), HelpAction }
            : new[]
            {
                MoreCablesAction(),
                new ModeCardAction("Remove VB-CABLE", "Opens VB-CABLE's own setup (Remove Driver) or tells where to remove it",
                    new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Full, null, _installer.RemoveCableAsync()))),
                HelpAction,
            };
    }

    /// <summary>Help on the Full control card: the table of setups (the same as the badge).</summary>
    private ModeCardAction HelpAction => _help ??= new ModeCardAction("Help", "The ways to set up the master and the cables, and which one runs now",
        new RelayCommand(ShowSetupTable));

    private ModeCardAction? _help;

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
            actions.Add(new("Remove", "Removes OmniControl APO and restores the audio settings (administrator)", new AsyncRelayCommand(RemoveForkAsync)));
        return actions;
    }

    /// <summary>Equalizer APO's Device Selector (administrator): where it is put on playback devices and, on its Capture tab, on microphones.</summary>
    private IAsyncRelayCommand ChooseDevicesCommand => _chooseDevices ??=
        new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Fast, null, _installer.ChooseEqualizerApoDevicesAsync()));

    private IAsyncRelayCommand? _chooseDevices;
    private ModeCardAction? _micDevices;
    private ModeCardAction? _speakerDevices;
    private ModeCardAction? _cableDevices;
    private ModeCardAction? _installApo;

    /// <summary>Install Equalizer APO: its own installer when it was placed next to the app, else its download page.</summary>
    private ModeCardAction InstallApoAction => _installApo ??= new("Install Equalizer APO", EngineInstaller.EqualizerApoSetup is null
            ? "Opens Equalizer APO's download page"
            : "Runs Equalizer APO's own installer (administrator)",
        new AsyncRelayCommand(InstallEqualizerApoAsync), Main: true);

    /// <summary>
    /// The master's step when Equalizer APO can't run it on the speakers in Full control: Choose devices when its Device Selector fixes
    /// it (tick the speakers on its Playback tab), Install when it is not installed; null when nothing is missing or nothing here fixes it.
    /// </summary>
    private ModeCardAction? MasterApoFix()
    {
        if (_mixer.SoundMode != SoundModes.Full || _engine.MasterFacts.ApoWhy is null) return null;
        var apo = _modes.Current?.Apo ?? ApoState.None;
        if (SoundModes.DeviceSelectorFixes(apo))
            return _speakerDevices ??= new ModeCardAction("Choose devices",
                "Opens Equalizer APO's Device Selector: tick your speakers on its Playback tab (administrator). A restart may be needed.",
                ChooseDevicesCommand);
        return apo.UpstreamInstalled ? null : InstallApoAction;
    }

    /// <summary>
    /// A microphone row's step when Equalizer APO can't run its effects in Fast mode: Choose devices when its Device Selector fixes it
    /// (not on this microphone, not registered, enhancements off), else nothing (Install is on the Fast mode card).
    /// </summary>
    private ModeCardAction? MicApoFix(string endpointId)
    {
        var apo = _modes.Current?.Apo ?? ApoState.None;
        if (SoundModes.MicApoMissing(apo, endpointId) is null || !SoundModes.DeviceSelectorFixes(apo)) return null;
        return _micDevices ??= new ModeCardAction("Choose devices",
            "Opens Equalizer APO's Device Selector: tick this microphone on its Capture tab (administrator). A restart may be needed.",
            ChooseDevicesCommand);
    }

    /// <summary>The Test on a playing program (either mode): records it, silences the original each way in turn, measures the delay; always restores.</summary>
    private async Task TestCaptureAsync(VolumeRow row)
    {
        var running = SoundModes.TestRunning();
        row.TestNote = running.Brief;
        row.TestTip = running.Full;
        var result = await _engine.TestAsync(row.Key);
        row.TestNote = result.Brief;
        row.TestTip = result.Full;
        _report(StatusMessage.Compose(result.Brief, result.Full));
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
        await _ask(new EngineMessage("Preview: install OmniControl APO", "Nothing changed yet. The install would:",
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
        await RunSetupAsync(SoundModes.Fast, "Installing… sound pauses briefly", _installer.InstallForkAsync());
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
        await RunSetupAsync(SoundModes.Fast, "Removing… sound pauses briefly", _installer.RemoveForkAsync());
    }

    /// <summary>Hotkey: the next sound mode, wrapping round; the footer says which and what it still needs.</summary>
    private void NextSoundMode()
    {
        var next = SoundModes.Next(_mixer.SoundMode);
        if (ModeWhy(next) is { } why)
        {
            _report(why);
            return;
        }
        SetSoundMode(next);
        var caution = _modes.StatusOf(next).FirstOrDefault(s => s.IsCaution);
        _report(caution is null ? $"Sound mode: {next.Name}" : $"Sound mode: {next.Name} ({caution.Text})");
    }
}
