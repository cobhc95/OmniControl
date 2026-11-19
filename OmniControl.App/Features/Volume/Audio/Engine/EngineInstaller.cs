using OmniControl.Core;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// How a setup step ended: one plain sentence; when it failed, the technical detail for "Copy details" (also in the log).
public sealed record SetupResult(string Text, bool Failed = false, string? Details = null);

/// The setup steps the effects engine offers (the steps at the foot of the master's Method menu). Nothing here changes the PC by itself:
/// Equalizer APO only through its Configurator when installed, otherwise its download page opens; VB-CABLE through its page. Every call
/// runs off the UI thread; failures come back as one plain sentence, with the detail in <see cref="LogPath"/>.
public sealed class EngineInstaller
{
    private const string LogName = "effects-setup-log.txt";

    /// The download pages a setup opens.
    private const string EqualizerApoPage = "https://sourceforge.net/projects/equalizerapo/", CablePage = "https://vb-audio.com/Cable/";

    private readonly EffectsSupportService _support;

    public EngineInstaller(EffectsSupportService support) => _support = support;

    /// The setup log: what OmniControl's setup steps did and why one failed.
    public static string LogPath => AppDataStore.PathOf(LogName);

    /// Equalizer APO is installed (its Configurator was found when the effects support was last read; for tooltips and confirmations).
    public bool EqualizerApoInstalled => _support.Current?.Apo.UpstreamDeviceSelector is not null;

    /// THE "install Equalizer APO" step, whichever place offers it (the master's Method menu, a Mixer row's fix link, an FX device
    /// folder's device menu, Easy): Equalizer APO's Configurator when it is installed (<see cref="ApoStatus.UpstreamConfigurator"/>, read
    /// now), otherwise its download page. <see cref="SetUpTip"/> says which.
    public Task<SetupResult> SetUpEqualizerApoAsync() => Task.Run(() =>
        ApoStatus.UpstreamConfigurator() is { } configurator
            ? RunConfigurator(configurator)
            : PageResult(EqualizerApoPage, "Opened Equalizer APO's download page. After installing it, tick your speakers in its Configurator (and microphones on its Capture tab)."));

    /// The tooltip of <see cref="SetUpEqualizerApoAsync"/>'s button.
    public string SetUpTip => EqualizerApoInstalled ? ConfiguratorTip : "Opens Equalizer APO's download page";

    public const string ConfiguratorTip = "Opens Equalizer APO's Configurator to choose the devices it runs on (administrator)";

    /// Equalizer APO is installed: its own Configurator puts it on playback devices and, on its Capture tab, on microphones.
    public Task<SetupResult> ChooseEqualizerApoDevicesAsync() => Task.Run(() =>
        ApoStatus.UpstreamConfigurator() is { } configurator
            ? RunConfigurator(configurator)
            : new SetupResult("Equalizer APO's Configurator wasn't found; open it from the Start menu."));

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
}
