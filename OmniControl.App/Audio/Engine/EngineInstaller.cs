using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>What a preview found: the full plan, whether it can be carried out, and its one-line summary (the first problem).</summary>
public sealed record SetupPreview(string Text, bool CanApply, string Summary);

/// <summary>How a setup step ended: one plain sentence; when it failed, the technical detail for "Copy details" (also in the log).</summary>
public sealed record SetupResult(string Text, bool Failed = false, string? Details = null);

/// <summary>
/// Installs and removes what the sound modes need (the setup buttons on the Sound mode cards). Nothing here changes the PC by itself:
/// Equalizer APO only through its own unmodified installer (when the owner placed it next to the app) or its Device Selector,
/// otherwise its download page opens; OmniControl APO (the optional fork, only when built) through OmniControl.exe itself, started
/// again with the administrator prompt in its <see cref="EngineSetupCommand"/> mode, so Windows never meets a second unknown
/// program; VB-CABLE only through VB-Audio's own installer, otherwise its page. Every call runs off the UI thread; failures come
/// back as one plain sentence, with the detail in <see cref="LogPath"/>.
/// </summary>
public sealed class EngineInstaller
{
    private const string CableSetupExe = "VBCABLE_Setup_x64.exe";
    private const string LogName = "effects-setup-log.txt";

    /// <summary>The names of the official Equalizer APO installer for 64-bit Windows, as its download page publishes them.</summary>
    private static readonly string[] EqualizerApoSetupNames = { "EqualizerAPO-x64-*.exe", "EqualizerAPO64-*.exe" };

    /// <summary>The download pages a setup falls back to.</summary>
    private const string EqualizerApoPage = "https://sourceforge.net/projects/equalizerapo/", CablePage = "https://vb-audio.com/Cable/";

    private readonly SoundModeService _modes;

    public EngineInstaller(SoundModeService modes) => _modes = modes;

    /// <summary>The setup log: what OmniControl's setup steps did and why one failed.</summary>
    public static string LogPath => AppDataStore.PathOf(LogName);

    /// <summary>The confirmations: what changes in one sentence, the administrator prompt, and how to undo it.</summary>
    public const string EqualizerApoConfirm =
        "Runs Equalizer APO's own installer. Some protected apps may not play while it is installed.\n" +
        "Windows will ask for administrator approval, and a restart may be needed. Uninstall it any time from Windows' Installed apps.";

    public const string ForkConfirm =
        "Installs OmniControl APO on the default playback device. Some protected apps may not play while it is installed.\n" +
        "Windows will ask for administrator approval, and sound pauses for a few seconds. Remove undoes it.";

    public const string ForkRemoveConfirm =
        "Removes OmniControl APO and puts the original audio settings back.\n" +
        "Windows will ask for administrator approval, and sound pauses for a few seconds.";

    private static string AppFolder => AppContext.BaseDirectory;
    private static string ApoSource => Path.Combine(AppFolder, ApoIds.AppFolderName);
    private static string ThirdParty(string name) => Path.Combine(AppFolder, "third_party", name);

    /// <summary>The official VB-CABLE installer, if the owner placed the unmodified package in third_party\vbcable.</summary>
    private static string CableSetup => Path.Combine(ThirdParty("vbcable"), CableSetupExe);

    /// <summary>The official Equalizer APO installer, if the owner placed it unmodified in third_party\equalizerapo (the newest name wins).</summary>
    public static string? EqualizerApoSetup
    {
        get
        {
            var folder = ThirdParty("equalizerapo");
            try
            {
                return Directory.Exists(folder)
                    ? EqualizerApoSetupNames.SelectMany(name => Directory.GetFiles(folder, name)).OrderDescending(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // unreadable folder: the download page is offered instead
            }
        }
    }

    /// <summary>OmniControl APO was built (native/EqualizerAPO/build.ps1) and copied next to the app: its advanced install can be offered.</summary>
    public static bool ForkBuilt => File.Exists(Path.Combine(ApoSource, ApoIds.DllName));

    public Task<SetupPreview> PreviewForkInstallAsync() => Task.Run(() =>
        _modes.Current?.DefaultDeviceId is { } device
            ? PreviewOf(ApoPlanner.PlanInstall(ApoSource, device))
            : new SetupPreview(NoDevice, false, NoDevice));

    public Task<SetupPreview> PreviewForkRemoveAsync() => Task.Run(() => PreviewOf(ApoPlanner.PlanUninstall()));

    private const string NoDevice = "There is no default playback device to install on.";

    private static SetupPreview PreviewOf(ApoPlan plan) =>
        new(plan.Describe(), plan.CanApply, plan.Problems.FirstOrDefault() ?? (plan.Steps.Count == 0 ? "There is nothing to change." : string.Empty));

    public Task<SetupResult> InstallForkAsync() =>
        RunSelfAsync("install", _modes.Current?.DefaultDeviceId, "OmniControl APO installed.", "The install didn't finish and was undone.");

    public Task<SetupResult> RemoveForkAsync() =>
        RunSelfAsync("uninstall", null, "OmniControl APO removed; the original audio settings are back.", "Removing didn't finish.");

    /// <summary>OmniControl.exe again, elevated, in its setup mode; its exit code and log say how it went.</summary>
    private Task<SetupResult> RunSelfAsync(string verb, string? device, string done, string failed) => Task.Run(() =>
    {
        if (Environment.ProcessPath is not { } self) return Fail("Couldn't find OmniControl's own program file.", "Environment.ProcessPath is empty.");
        var logStart = LogLength();
        var result = HelperProcess.RunElevated(self, EngineSetupCommand.Arguments(verb, LogPath, device, device is null ? null : ApoSource));
        _modes.Refresh();
        if (result.Failure != HelperFailure.None) return Outcome(result, "OmniControl's setup step", "use Equalizer APO's own installer instead");
        var log = LogSince(logStart);
        var details = $"{log}{Environment.NewLine}{result.Detail}";
        return result.ExitCode switch
        {
            EngineSetupCommand.Done => new SetupResult(done),
            _ when !log.Contains(EngineSetupCommand.StartLine) => Fail("OmniControl's setup step didn't start; Windows may have blocked it. Use Equalizer APO's own installer instead.", details),
            EngineSetupCommand.PlanProblems => Fail("Nothing was changed: the check before the change found a problem.", details),
            EngineSetupCommand.NotAdministrator => Fail("Nothing was changed: administrator rights weren't granted.", details),
            EngineSetupCommand.StepFailed => Fail(failed, details),
            _ => Fail($"OmniControl's setup step stopped unexpectedly (code {result.ExitCode}).", details),
        };
    });

    /// <summary>Install for Equalizer APO: its own installer when placed next to the app, else its download page.</summary>
    public Task<SetupResult> InstallEqualizerApoAsync() => Task.Run(() =>
    {
        if (EqualizerApoSetup is not { } setup)
            return PageResult(EqualizerApoPage, "Opened Equalizer APO's download page. After installing it, tick your speakers in its Device Selector (and microphones on its Capture tab).");
        var result = HelperProcess.RunElevated(setup, string.Empty);
        _modes.Refresh();
        if (result.Failure != HelperFailure.None) return Outcome(result, "Equalizer APO's installer", "run it yourself, or download it from its page");
        return new SetupResult(result.ExitCode == 0
            ? "Equalizer APO's installer finished. If it isn't found yet, restart Windows."
            : "Equalizer APO's installer was closed before it finished.");
    });

    /// <summary>Equalizer APO is installed: its own Device Selector puts it on playback devices and, on its Capture tab, on microphones.</summary>
    public Task<SetupResult> ChooseEqualizerApoDevicesAsync() => Task.Run(() =>
    {
        if (_modes.Current?.Apo.UpstreamDeviceSelector is not { } selector)
            return new SetupResult("Equalizer APO's Device Selector wasn't found; open it from the Start menu.");
        var result = HelperProcess.RunElevated(selector, string.Empty);
        _modes.Refresh();
        return result.Failure != HelperFailure.None
            ? Outcome(result, "Equalizer APO's Device Selector", "open it from the Start menu")
            : new SetupResult("Equalizer APO's Device Selector closed. If the device isn't found yet, restart Windows.");
    });

    /// <summary>VB-CABLE: its own installer when bundled (it asks for administrator rights itself), else its download page.</summary>
    public Task<SetupResult> InstallCableAsync() => RunCableSetupAsync(
        "Opened VB-CABLE's download page (VB-CABLE is donationware by VB-Audio, www.vb-cable.com).",
        "VB-CABLE's installer closed. Windows may need a restart before the cable appears. VB-CABLE is donationware by VB-Audio (www.vb-cable.com).");

    /// <summary>VB-CABLE's own installer also removes it ("Remove Driver"); without it, Windows' Apps list or VB-Audio's page does.</summary>
    public Task<SetupResult> RemoveCableAsync() => RunCableSetupAsync(
        "Remove VB-CABLE with its own setup program (Remove Driver) or from Windows' Installed apps.",
        "VB-CABLE's setup closed (its Remove Driver button removes the cable).");

    private Task<SetupResult> RunCableSetupAsync(string noSetup, string done) => Task.Run(() =>
    {
        if (!File.Exists(CableSetup)) return PageResult(CablePage, noSetup);
        var result = HelperProcess.RunElevated(CableSetup, string.Empty);
        _modes.Refresh();
        return result.Failure != HelperFailure.None ? Outcome(result, "VB-CABLE's setup", "download it from its page") : new SetupResult(done);
    });

    /// <summary>VB-Audio's page for more cables (VB-CABLE A+B and C+D are sold there and can't be bundled).</summary>
    public static SetupResult OpenCablePage() => PageResult(CablePage, "Opened VB-Audio's cable page (more cables: VB-CABLE A+B, C+D).");

    /// <summary>The setup is only a page: opens it and says so.</summary>
    private static SetupResult PageResult(string url, string opened) =>
        WebPage.Open(url) is { } error ? Fail("Couldn't open the download page.", error) : new SetupResult(opened);

    /// <summary>A program that did not end normally, in one plain sentence; <paramref name="instead"/> is the way round a block.</summary>
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

    private static long LogLength()
    {
        var file = new FileInfo(LogPath);
        return file.Exists ? file.Length : 0;
    }

    /// <summary>What the setup mode wrote to the log in this run.</summary>
    private static string LogSince(long start)
    {
        try
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Min(start, stream.Length), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty; // no log: the run is reported as not started
        }
    }
}
