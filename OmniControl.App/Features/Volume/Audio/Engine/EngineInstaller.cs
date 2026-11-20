using OmniControl.Core;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// How a setup step ended: one plain sentence; when it failed, the technical detail for "Copy details" (also in the log).
public sealed record SetupResult(string Text, bool Failed = false, string? Details = null);

/// The setup steps the effects engine offers (the master's Method menu, a Mixer row's fix, the Equalizer APO group, the first use of a
/// master effect). Equalizer APO is its own program: its official installer (shipped by OmniControl, <see cref="ApoInstaller"/>) installs
/// it and its own Device Selector chooses its devices; OmniControl runs either on request and then says where it stands on the master's
/// speakers. VB-CABLE through its page. One step runs at a time, off the UI thread; failures come back as one plain sentence, with the
/// detail in <see cref="LogPath"/>.
public sealed class EngineInstaller : IDisposable
{
    private const string LogName = "effects-setup-log.txt", CablePage = "https://vb-audio.com/Cable/", SetupFailedTitle = "The setup step didn't finish";

    /// The status line while Equalizer APO's installer runs.
    public const string Installing = "Installing Equalizer APO: tick your speakers in its Device Selector when it opens…";

    private readonly EffectsSupportService _support;
    private readonly MixerService _mixer;
    private readonly SemaphoreSlim _oneStep = new(1, 1);
    /// The first use of a master effect offered the install this run (UI thread): it is not offered again by itself.
    private bool _firstUseOffered;

    public EngineInstaller(EffectsSupportService support, MixerService mixer)
    {
        _support = support;
        _mixer = mixer;
    }

    /// The setup log: what OmniControl's setup steps did and why one failed.
    public static string LogPath => AppDataStore.PathOf(LogName);

    /// Shows how a step ended in the status line; a failure also opens a small message with "Copy details" and where the log is.
    public static async Task ShowAsync(SetupResult result, Action<string> report, Func<DialogMessage, Task<bool>> ask)
    {
        report(result.Text);
        if (result.Failed) await ask(new DialogMessage(SetupFailedTitle, $"{result.Text}\nDetails: {LogPath}", Details: result.Details));
    }

    /// "Install Equalizer APO…": its official installer, silently (one administrator prompt); its Device Selector opens at the end.
    public Task<SetupResult> InstallAsync() => OneStepAsync(speakers =>
    {
        var result = ApoInstaller.Run();
        if (result.Failure != HelperFailure.None)
            return Outcome(result, "Equalizer APO's installer", "get Equalizer APO from sourceforge.net/projects/equalizerapo");
        var apo = FreshLook(speakers);
        return !apo.UpstreamInstalled
            ? Fail("Equalizer APO's installer ended without installing it.", result.Detail)
            : new SetupResult($"{EffectsRules.ApoNowOn(apo, apo.PlaybackOf(speakers))} If its check showed a device that didn't start, restart Windows.");
    });

    /// "Equalizer APO devices…": its own Device Selector (administrator), where it is put on playback devices and microphones.
    public Task<SetupResult> ChooseEqualizerApoDevicesAsync() => OneStepAsync(speakers =>
    {
        if (ApoStatus.UpstreamConfigurator() is not { } selector) return new SetupResult("Equalizer APO's Device Selector wasn't found; open it from the Start menu.");
        var result = HelperProcess.RunElevated(selector, string.Empty, CoreTiming.ConfiguratorWaitMs);
        if (result.Failure != HelperFailure.None) return Outcome(result, "Equalizer APO's Device Selector", "open it from the Start menu");
        var apo = FreshLook(speakers);
        return new SetupResult($"Equalizer APO's Device Selector closed. {EffectsRules.ApoNowOn(apo, apo.PlaybackOf(speakers))}");
    });

    /// The first use of a master effect that Equalizer APO should run while it is not installed: the install is offered once per run,
    /// never run without the user's yes.
    public async Task FirstUseAsync(Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        if (_firstUseOffered) return;
        _firstUseOffered = true;
        if (!await ask(new DialogMessage("Install Equalizer APO?",
                "The master's effects run in Equalizer APO, which isn't installed.\nOmniControl brings its official installer: Windows asks once for administrator rights, then its Device Selector opens so you tick your speakers.",
                "Install")))
            return;
        report(Installing);
        await ShowAsync(await InstallAsync(), report, ask);
    }

    /// Runs one Equalizer APO step on a worker (one at a time), then asks for a fresh look; <paramref name="step"/> gets the master's speakers.
    private async Task<SetupResult> OneStepAsync(Func<string?, SetupResult> step)
    {
        var speakers = _mixer.MasterDeviceId ?? _mixer.DefaultDeviceId;
        await _oneStep.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => step(speakers)).ConfigureAwait(false);
        }
        finally
        {
            _oneStep.Release();
            _support.Refresh();
        }
    }

    /// Where Equalizer APO stands now on the master's speakers, read at once (the support service's next look comes later).
    private static ApoState FreshLook(string? speakers) =>
        ApoStatus.Read(speakers, speakers is null ? Array.Empty<string>() : new[] { speakers }, Array.Empty<string>());

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

    public void Dispose() => _oneStep.Dispose();
}
