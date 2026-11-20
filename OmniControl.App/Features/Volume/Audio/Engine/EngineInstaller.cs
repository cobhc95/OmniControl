using OmniControl.Core;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// How a setup step ended: one plain sentence; when it failed, the technical detail for "Copy details" (also in the log).
public sealed record SetupResult(string Text, bool Failed = false, string? Details = null);

/// The setup steps the effects engine offers (the master's Method menu, a Mixer row's fix, the Equalizer APO group, the first use of a
/// master effect). Equalizer APO: OmniControl's own copy (<see cref="ApoSetup"/>, one administrator prompt per step, each device change
/// restarts Windows audio and checks every device it is on, as Device Selector does) unless one is installed separately, which only its own Configurator changes; a build without the bundled
/// copy opens the download page. VB-CABLE through its page. One step runs at a time, off the UI thread; failures come back as one plain
/// sentence, with the detail in <see cref="LogPath"/>.
public sealed class EngineInstaller : IDisposable
{
    private const string LogName = "effects-setup-log.txt";

    /// The download pages a setup opens.
    private const string EqualizerApoPage = "https://sourceforge.net/projects/equalizerapo/", CablePage = "https://vb-audio.com/Cable/";

    private readonly EffectsSupportService _support;
    private readonly MixerService _mixer;
    private readonly SemaphoreSlim _oneStep = new(1, 1);
    /// The devices the first use of a master effect already tried this run (UI thread): a refused prompt is not asked again by itself.
    private readonly HashSet<string> _firstUseTried = new(StringComparer.OrdinalIgnoreCase);
    private bool _cleaned;

    public EngineInstaller(EffectsSupportService support, MixerService mixer)
    {
        _support = support;
        _mixer = mixer;
        support.Changed += OnSupportChanged;
    }

    /// The setup log: what OmniControl's setup steps did and why one failed.
    public static string LogPath => AppDataStore.PathOf(LogName);

    private ApoState Apo => _support.Current?.Apo ?? ApoState.None;

    /// Equalizer APO is installed separately (its Configurator was found when the effects support was last read).
    public bool EqualizerApoInstalled => Apo.UpstreamDeviceSelector is not null;

    /// THE "install Equalizer APO" step, whichever place offers it (the master's Method menu, a Mixer row's fix link, an FX device
    /// folder's device menu, Easy): a separate install's Configurator, else OmniControl's own copy put on the master's speakers (installed
    /// first when needed), else the download page. <see cref="SetUpTip"/> says which.
    public Task<SetupResult> SetUpEqualizerApoAsync()
    {
        if (ApoStatus.UpstreamConfigurator() is { } configurator) return Task.Run(() => RunConfigurator(configurator));
        if (Apo.Ownership.Owner == ApoOwner.Ours || Apo.Bundled) return PutOnAsync(_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId);
        return Task.Run(() => PageResult(EqualizerApoPage, "Opened Equalizer APO's download page. After installing it, tick your speakers in its Configurator (and microphones on its Capture tab)."));
    }

    /// The words and tooltip of <see cref="SetUpEqualizerApoAsync"/>'s button (it asks first when it changes Windows: <see cref="SetUpQuestion"/>).
    public string SetUpLabel => EqualizerApoInstalled ? "Install Equalizer APO"
        : Apo.Ownership.Owner == ApoOwner.Ours ? "Put Equalizer APO on the speakers…"
        : Apo.Bundled ? "Install Equalizer APO…"
        : "Install Equalizer APO";

    public string SetUpTip => EqualizerApoInstalled ? ConfiguratorTip
        : Apo.Ownership.Owner == ApoOwner.Ours || Apo.Bundled ? $"Puts Equalizer APO on {MasterName} (administrator); Windows audio restarts for a moment"
        : "Opens Equalizer APO's download page";

    /// The question <see cref="SetUpEqualizerApoAsync"/> asks before it changes Windows (OmniControl's own copy), naming the device and the
    /// audio restart; null when it only opens a program or a page.
    public DialogMessage? SetUpQuestion()
    {
        if (EqualizerApoInstalled || Apo.Ownership.Owner != ApoOwner.Ours && !Apo.Bundled) return null;
        var install = Apo.Ownership.Owner != ApoOwner.Ours;
        return new DialogMessage($"Put Equalizer APO on {MasterName}?",
            (install ? "OmniControl installs its copy of Equalizer APO, then attaches it to " : "OmniControl attaches Equalizer APO to ")
            + $"{MasterName} as Equalizer APO's own Device Selector does. Windows audio then restarts (all sound stops for a few seconds) and "
            + "OmniControl checks that Equalizer APO started there. Windows asks once for administrator rights.",
            install ? "Install and attach" : "Attach");
    }

    /// The master's speakers by Windows' name.
    private string MasterName => (_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId) is { } id ? NameOf(id) : "the speakers";

    public const string ConfiguratorTip = "Opens Equalizer APO's Configurator to choose the devices it runs on (administrator)";

    /// Equalizer APO is installed separately: its own Configurator puts it on playback devices and, on its Capture tab, on microphones.
    public Task<SetupResult> ChooseEqualizerApoDevicesAsync() => Task.Run(() =>
        ApoStatus.UpstreamConfigurator() is { } configurator
            ? RunConfigurator(configurator)
            : new SetupResult("Equalizer APO's Configurator wasn't found; open it from the Start menu."));

    /// The first use of a master effect that Equalizer APO should run on a device it is not on: OmniControl's copy is installed (when
    /// none is) and put on that device, behind one administrator prompt; tried once per device per run, so a refusal is not asked again.
    public async Task FirstUseAsync(string deviceId, Action<string> report)
    {
        if (!_firstUseTried.Add(deviceId)) return;
        report($"Setting up Equalizer APO for the master's effects on {NameOf(deviceId)}…");
        report((await PutOnAsync(deviceId)).Text);
    }

    /// Puts OmniControl's copy on a device (installing it first when there is none).
    private Task<SetupResult> PutOnAsync(string? deviceId)
    {
        if (deviceId is null) return Task.FromResult(new SetupResult("No speakers are plugged in to put Equalizer APO on."));
        var guid = ApoIds.DeviceKeyOf(deviceId);
        return StepAsync(Apo.Ownership.Owner == ApoOwner.Ours ? ApoSetup.Attach(guid) : ApoSetup.Install(guid), Runs(NameOf(deviceId)), NameOf(deviceId));
    }

    /// A tick or a troubleshooting option in the Equalizer APO device window: puts OmniControl's copy on the device with these options (again, when it is on).
    public Task<SetupResult> AttachAsync(ApoDeviceEntry device, ApoInstallState state) => StepAsync(ApoSetup.Attach(device.Guid, state), Runs(device.Name), device.Name);

    /// Takes OmniControl's copy off the device; its own effects come back.
    public Task<SetupResult> DetachAsync(ApoDeviceEntry device) =>
        StepAsync(ApoSetup.Detach(device.Guid), $"Equalizer APO is off {device.Name}; its own effects are back (Windows audio restarted and the other devices were checked).", device.Name);

    /// The group's install link: OmniControl's copy, on no device yet.
    public Task<SetupResult> InstallAsync() =>
        StepAsync(ApoSetup.Install(), "Equalizer APO is installed (Windows audio restarted); Choose devices in the master's Method menu puts it on devices.", null);

    /// "Remove OmniControl's Equalizer APO…": every device it is on gets its own effects back and the copy goes.
    public Task<SetupResult> RemoveAsync() =>
        StepAsync(ApoSetup.Remove(), "OmniControl's Equalizer APO is removed; every device has its own effects back.", null);

    private static string Runs(string device) => $"Equalizer APO runs on {device}: Windows audio restarted and its check passed.";

    /// One step of OmniControl's copy, as administrator, then a fresh look at what is in place; the outcome in one sentence.
    /// <param name="done">The sentence when it worked.</param>
    /// <param name="device">The device the step is about.</param>
    private async Task<SetupResult> StepAsync(string arguments, string done, string? device)
    {
        await _oneStep.WaitAsync().ConfigureAwait(false);
        try
        {
            var outcome = await Task.Run(() => ApoSetup.Run(arguments)).ConfigureAwait(false);
            _support.Refresh();
            return Says(outcome, done, device, arguments);
        }
        finally
        {
            _oneStep.Release();
        }
    }

    private static SetupResult Says(ApoOutcome outcome, string done, string? device, string arguments) => outcome switch
    {
        ApoOutcome.Done => new SetupResult(done),
        ApoOutcome.NotStarted => Fail($"Equalizer APO is on {device}, but its check failed: it didn't start there in any install mode tried.",
            $"OmniControl.exe {arguments}: no answer from Equalizer APO on {device} after the audio restart. Untick the device under Choose devices to give it its own effects back, or try its troubleshooting options."),
        ApoOutcome.OriginalNotStarted => new SetupResult($"Equalizer APO runs on {device}, but the device's own effects didn't start inside it (troubleshooting: untick Use original APO)."),
        ApoOutcome.NotChecked => new SetupResult($"Equalizer APO is on {device}; it couldn't be checked because the device is disabled or unplugged."),
        ApoOutcome.NothingInstalled => new SetupResult($"Equalizer APO is off {device}: with neither the pre-mix nor the post-mix APO ticked nothing is installed."),
        ApoOutcome.Unchanged =>new SetupResult(device is null ? "Equalizer APO is already in place; nothing was changed." : $"Equalizer APO is already set up this way on {device}; nothing was changed."),
        ApoOutcome.NeedsRestart => Fail("Done, but Windows audio didn't restart: it applies after the next restart of Windows.", ApoSetup.LastResult() ?? $"OmniControl.exe {arguments}: the audio restart failed."),
        ApoOutcome.Separate => new SetupResult("Equalizer APO is installed separately, so OmniControl leaves it as it is."),
        ApoOutcome.NotOurs => new SetupResult("OmniControl's Equalizer APO isn't installed, so nothing was changed."),
        ApoOutcome.NotRun => new SetupResult("Windows didn't run the step, so nothing was changed."),
        ApoOutcome.Unsupported => new SetupResult($"{device} can't use Equalizer APO: Windows gives remote devices no effect slots, so it was left as it is."),
        _ => Fail("Equalizer APO couldn't be set up.", ApoSetup.LastResult() ?? $"OmniControl.exe {arguments} failed."),
    };

    /// A separate Equalizer APO took over from OmniControl's copy: while OmniControl runs as administrator its leftovers (files and marker)
    /// go at once, without a prompt; otherwise Setup's next install or uninstall takes them.
    private void OnSupportChanged()
    {
        if (_cleaned || !AdministratorMode.IsElevated || Apo.Ownership is not { Owner: ApoOwner.Separate, Cleanup: not ApoCleanup.None }) return;
        _cleaned = true;
        _ = Guard.RunAsync("Equalizer APO: OmniControl's leftovers", () => StepAsync(ApoSetup.Clean(), string.Empty, null));
    }

    private string NameOf(string deviceId) =>
        _mixer.Devices.Concat(_mixer.RecordingDevices).FirstOrDefault(d => d.Id == deviceId)?.Name ?? "the speakers";

    /// Runs the Configurator and waits until it is closed (<see cref="CoreTiming.ConfiguratorWaitMs"/>), then reads again which devices
    /// have Equalizer APO, so every row and folder follows what was ticked there.
    private SetupResult RunConfigurator(string configurator)
    {
        var result = HelperProcess.RunElevated(configurator, string.Empty, CoreTiming.ConfiguratorWaitMs);
        _support.Refresh();
        return result.Failure != HelperFailure.None
            ? Outcome(result, "Equalizer APO's Configurator", "open it from the Start menu")
            : new SetupResult("Equalizer APO's Configurator closed. If the device isn't found yet, restart Windows.");
    }

    /// VB-CABLE: its download page (VB-CABLE is installed with VB-Audio's own installer).
    public Task<SetupResult> InstallCableAsync() => Task.Run(() =>
        PageResult(CablePage, "Opened VB-CABLE's download page (VB-CABLE is donationware by VB-Audio, www.vb-cable.com)."));

    /// VB-CABLE is removed with its own setup program or from Windows' Installed apps; VB-Audio's page opens.
    public Task<SetupResult> RemoveCableAsync() => Task.Run(() =>
        PageResult(CablePage, "Remove VB-CABLE with its own setup program (Remove Driver) or from Windows' Installed apps."));

    /// VB-Audio's page for more cables (VB-CABLE A+B and C+D are sold there).
    public static SetupResult OpenCablePage() => PageResult(CablePage, "Opened VB-Audio's cable page (more cables: VB-CABLE A+B, C+D).");

    /// The setup is only a page: opens it and says so.
    private static SetupResult PageResult(string url, string opened) =>
        WebPage.Open(url) is { } error ? Fail("Couldn't open the download page.", error) : new SetupResult(opened);

    /// A program that did not end normally, in one plain sentence; <paramref name="instead"/> is the way round a block.
    private static SetupResult Outcome(HelperResult result, string what, string instead) => result.Failure switch
    {
        HelperFailure.Cancelled => new SetupResult("Cancelled: nothing was changed."),
        HelperFailure.StillRunning => new SetupResult($"{what} is still open; finish there."),
        HelperFailure.Blocked => Fail($"Windows blocked {what} (Smart App Control); {instead}.", result.Detail),
        _ => Fail($"Couldn't start {what}.", result.Detail),
    };

    private static SetupResult Fail(string text, string details)
    {
        AppDataStore.AppendLog(LogName, $"{text} {details}");
        return new SetupResult(text, Failed: true, details);
    }

    public void Dispose()
    {
        _support.Changed -= OnSupportChanged;
        _oneStep.Dispose();
    }
}
