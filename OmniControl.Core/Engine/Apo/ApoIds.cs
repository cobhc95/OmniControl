namespace OmniControl.Core.Engine.Apo;

/// <summary>
/// The names and places of the OmniControl APO (the Equalizer APO fork in native/EqualizerAPO), defined once for the status check,
/// the installer and the config writer. They must match native/EqualizerAPO/helpers/RegistryHelper.h.
/// </summary>
public static class ApoIds
{
    /// <summary>OmniControl APO's own COM classes (pre-mix = LFX/SFX, post-mix = GFX/MFX/EFX), so it never collides with Equalizer APO.</summary>
    public const string PreMixClsid = "{27AB1524-86BE-4216-B86B-EC533B2B90B6}";
    public const string PostMixClsid = "{7F3F0D64-5AAB-432D-943C-C0B715C12DA0}";

    /// <summary>Upstream Equalizer APO's classes: if one sits on a device, OmniControl APO is not put there too.</summary>
    public const string UpstreamPreMixClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
    public const string UpstreamPostMixClsid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";

    /// <summary>HKLM key the APO reads (InstallPath, ConfigPath, EnableTrace, Child APOs) and where the installer keeps the originals.</summary>
    public const string AppKey = @"SOFTWARE\OmniControlAPO";
    public const string ChildApoKey = AppKey + @"\Child APOs";
    public const string UpstreamAppKey = @"SOFTWARE\EqualizerAPO";

    /// <summary>The Windows value that lets audiodg load an APO without a Microsoft signature.</summary>
    public const string AudioKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";
    public const string ProtectedAudioValue = "DisableProtectedAudioDG";

    /// <summary>Where Windows keeps each playback device's and each recording device's effects (FxProperties).</summary>
    public const string RenderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    public const string CaptureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";

    /// <summary>FxProperties value names of a device's effect slots, in Equalizer APO's order: LFX, GFX, SFX, MFX, EFX.</summary>
    public const string Lfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1", Gfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2",
        Sfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5", Mfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6",
        Efx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";

    public static readonly IReadOnlyList<string> FxSlots = new[] { Lfx, Gfx, Sfx, Mfx, Efx };

    /// <summary>The APO DLL's file name, in the app's "apo" folder (built) and in the install folder.</summary>
    public const string DllName = "OmniControlAPO.dll";
    public const string AppFolderName = "apo";

    /// <summary>The config file name inside ConfigPath (the APO reloads it when any file in that folder changes).</summary>
    public const string ConfigFileName = "config.txt";

    /// <summary>The config with no effects: the APO passes the sound through unchanged.</summary>
    public const string EmptyConfig = "# OmniControl master effects: none (sound passes unchanged)\r\n";

    /// <summary>OmniControl's own file in a config folder (the master's and the microphones' chains); config.txt pulls it in with <see cref="IncludeLine"/>.</summary>
    public const string ChainFileName = "OmniControl.txt";

    /// <summary>The one line OmniControl adds to config.txt (Equalizer APO's Include command), and the comment that marks it.</summary>
    public const string IncludeLine = "Include: " + ChainFileName;
    public const string IncludeMarker = "# Added by OmniControl for its effects; OmniControl removes it when they stop.";

    /// <summary>The marker earlier versions wrote; still recognised so a line a crashed older run left behind is taken out too.</summary>
    public const string PreviousIncludeMarker = "# Added by OmniControl for its master effects; OmniControl removes it when they stop.";

    /// <summary>The copy of config.txt taken before OmniControl first added its line (kept beside it, deleted once it matches again).</summary>
    public const string ConfigBackupFileName = "config.txt.omnicontrol-backup";

    /// <summary>Upstream Equalizer APO's own tool that puts it on playback devices (in its InstallPath).</summary>
    public const string UpstreamDeviceSelectorExe = "DeviceSelector.exe";

    /// <summary>Upstream Equalizer APO's engine in its InstallPath: without it Equalizer APO is not installed, whatever the registry says.</summary>
    public const string UpstreamDllName = "EqualizerAPO.dll";

    /// <summary>
    /// Where an APO's COM class and its audio-engine entry live; Device Selector's checkAPORegistration needs both for both classes
    /// (DeviceAPOInfo.cpp: HKCR\AudioEngine\AudioProcessingObjects and HKCR\CLSID).
    /// </summary>
    public const string ApoRegistrationKey = @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects";
    public const string ClsidKey = @"SOFTWARE\Classes\CLSID";

    /// <summary>A device's "audio enhancements off" value in FxProperties: while it is not 0, Windows loads no APO on that device.</summary>
    public const string DisableEnhancements = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OmniControl APO");

    public static string ConfigFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OmniControl", "APO");

    /// <summary>The device's registry key name ({guid}) from its endpoint id ("{0.0.0.00000000}.{guid}").</summary>
    public static string DeviceKeyOf(string endpointId)
    {
        var at = endpointId.LastIndexOf("}.{", StringComparison.Ordinal);
        return at < 0 ? endpointId : endpointId[(at + 2)..];
    }

    public static bool IsOurs(string? clsid) =>
        string.Equals(clsid, PreMixClsid, StringComparison.OrdinalIgnoreCase) || string.Equals(clsid, PostMixClsid, StringComparison.OrdinalIgnoreCase);

    public static bool IsUpstream(string? clsid) =>
        string.Equals(clsid, UpstreamPreMixClsid, StringComparison.OrdinalIgnoreCase) || string.Equals(clsid, UpstreamPostMixClsid, StringComparison.OrdinalIgnoreCase);
}
