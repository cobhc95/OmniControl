namespace OmniControl.Core.Engine.Apo;

/// The names and places of Equalizer APO, defined once for the status check, its installer and the config writer.
public static class ApoIds
{
    /// Equalizer APO's COM classes (pre-mix and post-mix), as they sit in a device's effect slots.
    public const string UpstreamPreMixClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
    public const string UpstreamPostMixClsid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";

    /// HKLM key Equalizer APO keeps its InstallPath and ConfigPath in.
    public const string UpstreamAppKey = @"SOFTWARE\EqualizerAPO";

    /// The Windows value that lets audiodg load an APO without a Microsoft signature.
    public const string AudioKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";
    public const string ProtectedAudioValue = "DisableProtectedAudioDG";

    /// Where Windows keeps each playback device's and each recording device's effects (FxProperties).
    public const string RenderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    public const string CaptureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";

    /// FxProperties value names of a device's effect slots, in Equalizer APO's order: LFX, GFX, SFX, MFX, EFX.
    public static readonly IReadOnlyList<string> FxSlots = new[]
    {
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1", "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2", "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5",
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6", "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7",
    };

    /// The config file name inside ConfigPath (the APO reloads it when any file in that folder changes).
    public const string ConfigFileName = "config.txt";

    /// OmniControl's own file in a config folder (the master's and the microphones' chains); config.txt pulls it in with <see cref="IncludeLine"/>.
    public const string ChainFileName = "OmniControl.txt";

    /// The one line OmniControl adds to config.txt (Equalizer APO's Include command), and the comment that marks it.
    public const string IncludeLine = "Include: " + ChainFileName;
    public const string IncludeMarker = "# Added by OmniControl for its effects; OmniControl removes it when they stop.";

    /// The marker earlier versions wrote; still recognised so a line a crashed older run left behind is taken out too.
    public const string PreviousIncludeMarker = "# Added by OmniControl for its master effects; OmniControl removes it when they stop.";

    /// The copy of config.txt taken before OmniControl first added its line (kept beside it, deleted once it matches again).
    public const string ConfigBackupFileName = "config.txt.omnicontrol-backup";

    /// Equalizer APO's own tool that puts it on devices (in its InstallPath).
    public const string UpstreamDeviceSelectorExe = "DeviceSelector.exe";

    /// Equalizer APO's engine in its InstallPath: without it Equalizer APO is not installed, whatever the registry says.
    public const string UpstreamDllName = "EqualizerAPO.dll";

    /// Equalizer APO's own folder name under Program Files: where it is looked for when its InstallPath value is missing.
    public const string UpstreamFolderName = "EqualizerAPO";

    /// The first Equalizer APO that runs a dry/wet mix as OmniControl writes it: a Copy command whose target is a new name keeps a virtual
    /// channel (CopyFilter.cpp at bbfcc3e) that later Copy lines can read.
    public static readonly Version BlendVersion = new(1, 2);

    /// A device's own format in its Properties key (PKEY_AudioEngine_DeviceFormat): a WAVEFORMATEX after an 8-byte header.
    public const string DeviceFormatValue = "{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0";

    /// Where nChannels sits in <see cref="DeviceFormatValue"/>: the 8-byte header, then wFormatTag (2 bytes).
    public const int DeviceFormatChannelsOffset = 10;

    /// A device's connector name in its Properties key (Device Selector's Connector column, "Speakers").
    public const string ConnectionNameValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";

    /// Where an APO's COM class and its audio-engine entry live; Device Selector's checkAPORegistration needs both for both classes
    /// (DeviceAPOInfo.cpp: HKCR\AudioEngine\AudioProcessingObjects and HKCR\CLSID).
    public const string ApoRegistrationKey = @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects";
    public const string ClsidKey = @"SOFTWARE\Classes\CLSID";

    /// A device's "audio enhancements off" value in FxProperties: while it is not 0, Windows loads no APO on that device.
    public const string DisableEnhancements = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    /// The device's registry key name ({guid}) from its endpoint id ("{0.0.0.00000000}.{guid}").
    public static string DeviceKeyOf(string endpointId)
    {
        var at = endpointId.LastIndexOf("}.{", StringComparison.Ordinal);
        return at < 0 ? endpointId : endpointId[(at + 2)..];
    }

    /// Device Selector's test: the text parses as a braced class id (CLSIDFromString) equal to one of Equalizer APO's.
    public static bool IsUpstream(string? clsid) =>
        Guid.TryParseExact(clsid, "B", out var id) && (id == Guid.Parse(UpstreamPreMixClsid) || id == Guid.Parse(UpstreamPostMixClsid));
}
