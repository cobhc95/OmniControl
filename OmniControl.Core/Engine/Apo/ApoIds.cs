namespace OmniControl.Core.Engine.Apo;

/// The names and places of the user's Equalizer APO, defined once for the status check and the config writer.
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
    public const string Lfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1", Gfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2",
        Sfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5", Mfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6",
        Efx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";

    public static readonly IReadOnlyList<string> FxSlots = new[] { Lfx, Gfx, Sfx, Mfx, Efx };

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

    /// Upstream Equalizer APO's own tool that puts it on playback devices (in its InstallPath).
    public const string UpstreamDeviceSelectorExe = "DeviceSelector.exe";

    /// Upstream Equalizer APO's engine in its InstallPath: without it Equalizer APO is not installed, whatever the registry says.
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

    /// Where an APO's COM class and its audio-engine entry live; Device Selector's checkAPORegistration needs both for both classes
    /// (DeviceAPOInfo.cpp: HKCR\AudioEngine\AudioProcessingObjects and HKCR\CLSID).
    public const string ApoRegistrationKey = @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects";
    public const string ClsidKey = @"SOFTWARE\Classes\CLSID";

    /// A device's "audio enhancements off" value in FxProperties: while it is not 0, Windows loads no APO on that device.
    public const string DisableEnhancements = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    /// Equalizer APO's Uninstall entry: only its own installer writes it, so it marks a separately installed copy.
    public const string UpstreamUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EqualizerAPO";

    /// Where each attached device's original effects and options are kept (DeviceAPOInfo.cpp childApoPath); the APO reads it to load the
    /// device's own APO as its child.
    public const string ChildApoKey = UpstreamAppKey + @"\Child APOs";

    /// The values Equalizer APO's Device Selector writes per device under <see cref="ChildApoKey"/> (DeviceAPOInfo.cpp).
    public const string PreMixChild = "PreMixChild", PostMixChild = "PostMixChild", AllowSilentBuffer = "AllowSilentBufferModification",
        DisableAutoAdjust = "DisableAutomaticAdjustment", InstallVersionValue = "Version", InstallVersion = "2";

    /// A slot that was empty (<see cref="NoValue"/>) or a device with no FxProperties key at all (<see cref="NoKey"/>) before Equalizer APO came.
    public const string NoKey = "!KEY", NoValue = "!VALUE";

    /// The name Device Selector gives an FxProperties key it had to create.
    public const string FxTitleValue = "{b725f130-47ef-101a-a5f1-02608c9eebac},10";

    /// The processing modes of the SFX, MFX and EFX slots, and the default mode Device Selector writes when none is there.
    public const string SfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5", MfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},6",
        EfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7", DefaultProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";

    /// The multi-mode SFX, MFX and EFX slots: a driver that fills any of them is not given LFX/GFX by default.
    public static readonly IReadOnlyList<string> MultiFxSlots = new[]
    {
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},13", "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14", "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},15",
    };

    /// A device's Properties values: Windows 11 marks a combined Bluetooth device (EFX does not work there), its interface and its own name.
    public const string CombinedDeviceValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},41", ConnectionNameValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2",
        DeviceNameValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    /// A device's state in its MMDevices key (DEVICE_STATE_*); Windows writes 0x10000000 for a disabled device.
    public const string DeviceStateValue = "DeviceState";
    public const uint StateDisabled = 0x2, StateNotPresent = 0x4, StateUnplugged = 0x8, StateDisabledInRegistry = 0x10000000;

    /// While this value names a pipe, the APO reports there each time it starts on a device (EqualizerAPO.cpp sendMessage).
    public const string DeviceTestPipeValue = "DeviceTestPipeName";

    /// OmniControl's own copy: its folder under Program Files (audiodg can read it and only administrators can change it) and the key that
    /// says it is OmniControl's (<see cref="OwnPathValue"/> = the folder), with the devices it attached and how each check ended.
    public const string OwnFolder = @"OmniControl\EqualizerAPO", OwnMarkerKey = @"SOFTWARE\OmniControl\EqualizerApo", OwnPathValue = "Path",
        OwnDevicesKey = OwnMarkerKey + @"\Devices";

    /// What the marker kept from before OmniControl's copy was installed, put back when it is removed (a value that was not there is "").
    public const string OwnPreviousPrefix = "Previous ";

    /// The device's registry key name ({guid}) from its endpoint id ("{0.0.0.00000000}.{guid}").
    public static string DeviceKeyOf(string endpointId)
    {
        var at = endpointId.LastIndexOf("}.{", StringComparison.Ordinal);
        return at < 0 ? endpointId : endpointId[(at + 2)..];
    }

    public static bool IsUpstream(string? clsid) =>
        string.Equals(clsid, UpstreamPreMixClsid, StringComparison.OrdinalIgnoreCase) || string.Equals(clsid, UpstreamPostMixClsid, StringComparison.OrdinalIgnoreCase);
}
