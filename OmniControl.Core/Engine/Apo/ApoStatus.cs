using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace OmniControl.Core.Engine.Apo;

/// <summary>
/// One audio endpoint's effect slots (a playback device or a microphone), read with Equalizer APO's own Device Selector checks
/// (DeviceAPOInfo.cpp at bbfcc3e): which APO sits in its LFX/GFX/SFX/MFX/EFX slots and whether its "audio enhancements" are off.
/// </summary>
/// <param name="EndpointId">The endpoint id ("{0.0.0.00000000}.{guid}" playback, "{0.0.1.00000000}.{guid}" recording).</param>
/// <param name="OursOn">OmniControl APO is in one of its slots.</param>
/// <param name="UpstreamOn">Equalizer APO is in one of its slots.</param>
/// <param name="EnhancementsOff">"Audio enhancements" are off on it, so Windows loads no APO there.</param>
/// <param name="Channels">Its channel count (its own format, as Windows keeps it); stereo when it can't be read.</param>
public sealed record ApoEndpoint(string EndpointId, bool OursOn, bool UpstreamOn, bool EnhancementsOff, int Channels = ApoEndpoint.Stereo)
{
    public const int Stereo = 2;
}

/// <summary>
/// What is in place for effects in Windows' audio engine on this PC (read only): the user's own Equalizer APO (upstream) and
/// OmniControl APO (the fork), the default playback device's slots (master effects) and every microphone's (microphone effects).
/// OmniControl uses the user's Equalizer APO on a device where it is active; OmniControl APO is only the optional route for a PC
/// without Equalizer APO. The checks are Equalizer APO's own (Device Selector): the protected audio path value, both classes
/// registered, the device's effect slots and its "enhancements off" value.
/// </summary>
/// <param name="Registered">OmniControl APO's classes are registered (as Device Selector checks) and its DLL file exists.</param>
/// <param name="ProtectedAudioDisabled">DisableProtectedAudioDG is 1, so Windows loads an APO without a Microsoft signature (both need it).</param>
/// <param name="ConfigFolder">Where OmniControl APO reads config.txt; null when not installed.</param>
/// <param name="UpstreamInstalled">Equalizer APO itself is installed: its InstallPath holds EqualizerAPO.dll (a key or config folder left
/// behind by an uninstall does not count).</param>
/// <param name="UpstreamRegistered">Equalizer APO's two classes are registered with COM and the audio engine (Device Selector's checkAPORegistration).</param>
/// <param name="UpstreamConfigFolder">Equalizer APO's config folder (its ConfigPath) when that folder exists; also read when only an
/// uninstall's leftovers remain, so OmniControl can take its own line out of it.</param>
/// <param name="UpstreamDeviceSelector">Equalizer APO's Device Selector program when it exists.</param>
/// <param name="Output">The default playback device's slots; null when there is no default playback device.</param>
/// <param name="Microphones">Every recording device's slots (virtual cables left out), in Windows' order.</param>
public sealed record ApoState(
    bool Registered, bool ProtectedAudioDisabled, string? ConfigFolder,
    bool UpstreamInstalled, bool UpstreamRegistered, string? UpstreamConfigFolder, string? UpstreamDeviceSelector,
    ApoEndpoint? Output, IReadOnlyList<ApoEndpoint> Microphones)
{
    public static readonly ApoState None = new(false, false, null, false, false, null, null, null, Array.Empty<ApoEndpoint>());

    /// <summary>
    /// Every playback device's slots, virtual cables included, in Windows' order: where Equalizer APO runs on a device that is not the
    /// default (the real speakers while a cable is the default, a cable a program's chain is written for). Empty when not read.
    /// </summary>
    public IReadOnlyList<ApoEndpoint> Playback { get; init; } = Array.Empty<ApoEndpoint>();

    /// <summary>Equalizer APO's version (its EqualizerAPO.dll); null when it is not installed or the file has none.</summary>
    public Version? UpstreamVersion { get; init; }

    /// <summary>
    /// The APO that reads <paramref name="folder"/> can blend an effect's result with the sound before it (a Mix below 100 %, see
    /// <see cref="ApoIds.BlendVersion"/>): OmniControl APO always; Equalizer APO from that version on, or when its version can't be read.
    /// </summary>
    public bool BlendsIn(string? folder) =>
        !string.Equals(folder, UpstreamConfigFolder, StringComparison.OrdinalIgnoreCase) || UpstreamVersion is null || UpstreamVersion >= ApoIds.BlendVersion;

    /// <summary>The slots of the playback device <paramref name="endpointId"/>: from <see cref="Playback"/>, else <see cref="Output"/> when it is that device; null when not looked at.</summary>
    public ApoEndpoint? PlaybackOf(string? endpointId) =>
        endpointId is null ? null
        : Playback.FirstOrDefault(p => string.Equals(p.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase))
          ?? (string.Equals(Output?.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase) ? Output : null);

    /// <summary>OmniControl APO sits on the default playback device.</summary>
    public bool OnDevice => Output?.OursOn == true;

    /// <summary>"Audio enhancements" are off on the default playback device.</summary>
    public bool EnhancementsOff => Output?.EnhancementsOff == true;

    /// <summary>Equalizer APO is installed, registered, loaded by Windows and has its config folder: it runs wherever it is on a device.</summary>
    public bool UpstreamUsable => UpstreamInstalled && UpstreamRegistered && ProtectedAudioDisabled && UpstreamConfigFolder is not null;

    /// <summary>OmniControl APO is registered, loaded by Windows and has its config folder.</summary>
    public bool ForkUsable => Registered && ProtectedAudioDisabled && ConfigFolder is not null;

    /// <summary>Everything needed for OmniControl APO to run on the default device.</summary>
    public bool Ready => Output is { } o && ForkUsable && o.OursOn && !o.EnhancementsOff;

    /// <summary>
    /// The config folder whose APO processes <paramref name="endpoint"/> (Equalizer APO first, then OmniControl APO), or null when
    /// neither runs there.
    /// </summary>
    public string? FolderFor(ApoEndpoint endpoint) =>
        endpoint.EnhancementsOff ? null
        : UpstreamUsable && endpoint.UpstreamOn ? UpstreamConfigFolder
        : ForkUsable && endpoint.OursOn ? ConfigFolder
        : null;

    /// <summary>The slots of the recording device <paramref name="endpointId"/>, or null when it was not looked at (unplugged, a cable).</summary>
    public ApoEndpoint? MicrophoneOf(string endpointId) =>
        Microphones.FirstOrDefault(m => string.Equals(m.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every config folder OmniControl may have written to (so its include can be taken out of each).</summary>
    public IEnumerable<string> ConfigFolders => new[] { UpstreamConfigFolder, ConfigFolder }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Some of OmniControl APO is installed (so there is something to remove).</summary>
    public bool AnyInstalled => Registered || OnDevice || ConfigFolder is not null;

    /// <summary>Compares the microphones by content, so a re-check that found the same state raises no change.</summary>
    public bool Equals(ApoState? other) =>
        other is not null && Registered == other.Registered && ProtectedAudioDisabled == other.ProtectedAudioDisabled
        && ConfigFolder == other.ConfigFolder && UpstreamInstalled == other.UpstreamInstalled && UpstreamRegistered == other.UpstreamRegistered
        && UpstreamConfigFolder == other.UpstreamConfigFolder && UpstreamDeviceSelector == other.UpstreamDeviceSelector && UpstreamVersion == other.UpstreamVersion
        && Output == other.Output && Microphones.SequenceEqual(other.Microphones) && Playback.SequenceEqual(other.Playback);

    public override int GetHashCode() => HashCode.Combine(Registered, ProtectedAudioDisabled, ConfigFolder, UpstreamInstalled, Output, Microphones.Count);
}

/// <summary>Reads <see cref="ApoState"/> from the registry (64-bit view). Never writes.</summary>
public static class ApoStatus
{
    /// <param name="defaultEndpointId">The default playback device's endpoint id, or null when there is none.</param>
    /// <param name="playbackIds">The endpoint ids of every playback device (cables included) whose slots are wanted.</param>
    /// <param name="microphoneIds">The endpoint ids of the recording devices whose slots are wanted.</param>
    public static ApoState Read(string? defaultEndpointId, IEnumerable<string> playbackIds, IEnumerable<string> microphoneIds)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var registered = DllOf(machine, ApoIds.PreMixClsid) is { } dll && File.Exists(dll)
                && IsRegistered(machine, ApoIds.PreMixClsid, ApoIds.PostMixClsid);
            using var audio = machine.OpenSubKey(ApoIds.AudioKey);
            var dg = audio?.GetValue(ApoIds.ProtectedAudioValue) is int v && v == 1;
            using var app = machine.OpenSubKey(ApoIds.AppKey);
            var config = app?.GetValue("ConfigPath") as string;
            var installFolder = UpstreamInstallFolder(machine);
            using var upstream = machine.OpenSubKey(ApoIds.UpstreamAppKey);
            var upstreamConfig = upstream?.GetValue("ConfigPath") is string path && Directory.Exists(path) ? path : null;
            var selector = ConfiguratorIn(installFolder);
            return new ApoState(registered, dg, config,
                installFolder is not null, IsRegistered(machine, ApoIds.UpstreamPreMixClsid, ApoIds.UpstreamPostMixClsid),
                upstreamConfig, selector,
                defaultEndpointId is null ? null : EndpointOf(machine, defaultEndpointId),
                microphoneIds.Select(id => EndpointOf(machine, id)).ToList())
            {
                Playback = playbackIds.Select(id => EndpointOf(machine, id)).ToList(),
                UpstreamVersion = installFolder is null ? null : VersionOf(Path.Combine(installFolder, ApoIds.UpstreamDllName)),
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return ApoState.None; // not allowed to read: reported as not installed, which only offers the install flow
        }
    }

    /// <summary>One endpoint's slots and enhancements value, the same checks for playback and recording devices.</summary>
    private static ApoEndpoint EndpointOf(RegistryKey machine, string endpointId)
    {
        var key = EndpointKey(machine, ApoIds.DeviceKeyOf(endpointId));
        var slots = SlotsOf(machine, key);
        return new ApoEndpoint(endpointId, slots.Any(ApoIds.IsOurs), slots.Any(ApoIds.IsUpstream), EnhancementsOffOn(machine, key), ChannelsOf(machine, key));
    }

    /// <summary>The device's channel count from its own format (<see cref="ApoIds.DeviceFormatValue"/>); stereo when it can't be read.</summary>
    private static int ChannelsOf(RegistryKey machine, string endpointKey)
    {
        using var properties = machine.OpenSubKey($@"{endpointKey}\Properties");
        return properties?.GetValue(ApoIds.DeviceFormatValue) is byte[] format && format.Length >= ApoIds.DeviceFormatChannelsOffset + sizeof(ushort)
            && BitConverter.ToUInt16(format, ApoIds.DeviceFormatChannelsOffset) is var channels and > 0
            ? channels
            : ApoEndpoint.Stereo;
    }

    /// <summary>A program file's version (major.minor.build), or null when it has none.</summary>
    private static Version? VersionOf(string file)
    {
        var info = FileVersionInfo.GetVersionInfo(file);
        return info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0 ? null : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    /// <summary>
    /// The MMDevices key of a device ({guid}): under Render when it is a playback device, else under Capture (Equalizer APO's own
    /// rule, DeviceAPOInfo::load: the Render key is tried first, a device that is not there is a recording device).
    /// </summary>
    internal static string EndpointKey(RegistryKey machine, string deviceGuid)
    {
        using var render = machine.OpenSubKey($@"{ApoIds.RenderKey}\{deviceGuid}");
        return render is not null ? $@"{ApoIds.RenderKey}\{deviceGuid}" : $@"{ApoIds.CaptureKey}\{deviceGuid}";
    }

    /// <summary>
    /// Equalizer APO's install folder when its program files are really there (EqualizerAPO.dll in its InstallPath, else in its usual
    /// Program Files folder when that value is missing or stale), else null. Its uninstaller leaves HKLM\SOFTWARE\EqualizerAPO and the
    /// config folder behind, so neither alone means it is installed.
    /// </summary>
    internal static string? UpstreamInstallFolder(RegistryKey machine)
    {
        using var upstream = machine.OpenSubKey(ApoIds.UpstreamAppKey);
        var usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ApoIds.UpstreamFolderName);
        return new[] { upstream?.GetValue("InstallPath") as string, usual }
            .FirstOrDefault(folder => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, ApoIds.UpstreamDllName)));
    }

    /// <summary>
    /// Equalizer APO's Configurator (its DeviceSelector.exe) when Equalizer APO is installed (<see cref="UpstreamInstallFolder"/>), read
    /// now; null otherwise. Every "install Equalizer APO" step opens it instead of the setup when it exists.
    /// </summary>
    public static string? UpstreamConfigurator()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return ConfiguratorIn(UpstreamInstallFolder(machine));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // not allowed to read: treated as not installed, which only offers the setup
        }
    }

    private static string? ConfiguratorIn(string? installFolder) =>
        installFolder is not null && Path.Combine(installFolder, ApoIds.UpstreamDeviceSelectorExe) is var exe && File.Exists(exe) ? exe : null;

    /// <summary>Device Selector's checkAPORegistration: both classes under CLSID and under the audio engine's APO list.</summary>
    private static bool IsRegistered(RegistryKey machine, string preMix, string postMix) =>
        new[] { preMix, postMix }.All(clsid => HasKey(machine, $@"{ApoIds.ClsidKey}\{clsid}") && HasKey(machine, $@"{ApoIds.ApoRegistrationKey}\{clsid}"));

    private static bool HasKey(RegistryKey machine, string path)
    {
        using var key = machine.OpenSubKey(path);
        return key is not null;
    }

    private static string? DllOf(RegistryKey machine, string clsid)
    {
        using var key = machine.OpenSubKey($@"{ApoIds.ClsidKey}\{clsid}\InprocServer32");
        return key?.GetValue(null) as string;
    }

    /// <summary>Device Selector's enhancementsDisabled: the device's "audio enhancements off" value is present and not 0.</summary>
    private static bool EnhancementsOffOn(RegistryKey machine, string endpointKey)
    {
        using var fx = machine.OpenSubKey($@"{endpointKey}\FxProperties");
        return fx?.GetValue(ApoIds.DisableEnhancements) is int off && off != 0;
    }

    /// <summary>The class ids in a device's five effect slots (null where a slot is empty); <paramref name="endpointKey"/> from <see cref="EndpointKey"/>.</summary>
    internal static string?[] SlotsOf(RegistryKey machine, string endpointKey)
    {
        using var fx = machine.OpenSubKey($@"{endpointKey}\FxProperties");
        return ApoIds.FxSlots.Select(name => fx?.GetValue(name) as string).ToArray();
    }
}
