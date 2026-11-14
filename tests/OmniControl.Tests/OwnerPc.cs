using System.Text.Json;
using OmniControl.App.Audio;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.Tests;

/// <summary>
/// The owner's PC as read on 14 Nov 2026 (endpoints from Core's own enumeration) and the relevant part of the owner's mixer.json
/// (program paths shortened): one VB-CABLE with two inputs ("CABLE Input" and "CABLE In 16ch") and one output, a Realtek speaker,
/// Full control, Helium and Spotify with an equaliser each, Claude with effects off, the master with an equaliser.
/// </summary>
internal static class OwnerPc
{
    public const string CableInput = "{0.0.0.00000000}.{10207fab-95bc-4b24-9c2d-d29fbf19f212}";
    public const string Cable16 = "{0.0.0.00000000}.{1e2139d3-cd74-466f-804d-14f389aeb0df}";
    public const string Speaker = "{0.0.0.00000000}.{35486a40-aa4f-4ae2-814d-37fb8824aaa2}";
    public const string CableOutput = "{0.0.1.00000000}.{cbe87cfe-db19-4e93-845a-1c9fbc11286c}";
    public const string StereoMix = "{0.0.1.00000000}.{8d393aab-2422-497a-a2dd-142d1e16514e}";
    public const string MicArray = "{0.0.1.00000000}.{8d583f4c-56c5-4347-a56d-cc27105df83b}";

    public const string Helium = @"c:\apps\helium\application\chrome.exe";
    public const string Claude = @"c:\program files\windowsapps\anthropicpbc.claude_x64__fnn82j28hfe8t\app\claude.exe";
    public const string Spotify = @"c:\apps\spotify\spotify.exe";

    /// <summary>Windows' default is the bus cable, as it is while Full control runs.</summary>
    public static IReadOnlyList<AudioDevice> Playback { get; } = new[]
    {
        new AudioDevice(CableInput, "CABLE Input (VB-Audio Virtual Cable)", true),
        new AudioDevice(Cable16, "CABLE In 16ch (VB-Audio Virtual Cable)", false),
        new AudioDevice(Speaker, "Speaker (Realtek(R) Audio)", false),
    };

    public static IReadOnlyList<AudioDevice> Recording { get; } = new[]
    {
        new AudioDevice(StereoMix, "Stereo Mix (Realtek(R) Audio)", false),
        new AudioDevice(MicArray, "Microphone Array (AMD Audio Device)", false),
        new AudioDevice(CableOutput, "CABLE Output (VB-Audio Virtual Cable)", false),
    };

    /// <summary>What the probe reports for this PC (the cables found by the same code the app uses).</summary>
    public static AudioEffectsSupport Support { get; } =
        new(true, VirtualCables.Find(Playback, Recording), ApoState.None, CableInput);

    public const string CableA = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-00000000000a}";
    public const string CableAOutput = "{0.0.1.00000000}.{aaaaaaaa-0000-0000-0000-00000000000b}";

    /// <summary>The same PC with a second, independent cable pair (VB-CABLE A): what a processed program needs in Full control.</summary>
    public static IReadOnlyList<AudioDevice> PlaybackTwoPairs { get; } =
        Playback.Append(new AudioDevice(CableA, "CABLE-A Input (VB-Audio Cable A)", false)).ToArray();

    public static IReadOnlyList<AudioDevice> RecordingTwoPairs { get; } =
        Recording.Append(new AudioDevice(CableAOutput, "CABLE-A Output (VB-Audio Cable A)", false)).ToArray();

    public static AudioEffectsSupport SupportTwoPairs { get; } =
        new(true, VirtualCables.Find(PlaybackTwoPairs, RecordingTwoPairs), ApoState.None, CableInput);

    /// <summary>The same PC once the speakers are Windows' default again (the master in Equalizer APO: no bus owns the default).</summary>
    public static IReadOnlyList<AudioDevice> PlaybackSpeakerDefault { get; } =
        Playback.Select(d => d with { IsDefault = d.Id == Speaker }).ToArray();

    public static IReadOnlyList<AudioDevice> PlaybackTwoPairsSpeakerDefault { get; } =
        PlaybackTwoPairs.Select(d => d with { IsDefault = d.Id == Speaker }).ToArray();

    /// <summary>Equalizer APO's config folder in the made-up APO states (nothing is ever written there: the tests use a fake store).</summary>
    public const string ApoFolder = @"C:\Program Files\EqualizerAPO\config";

    /// <summary>
    /// Equalizer APO installed, registered and loaded, ticked on the Realtek speaker (the one-time Device Selector step) and, when
    /// <paramref name="onCableA"/>, on "CABLE-A Input" too. Windows' default as given.
    /// </summary>
    public static ApoState ApoOnSpeaker(bool onCableA = false, string defaultId = Speaker) =>
        new(false, true, null, true, true, ApoFolder, null, new ApoEndpoint(defaultId, false, defaultId == Speaker, false), Array.Empty<ApoEndpoint>())
        {
            Playback = new[]
            {
                new ApoEndpoint(CableInput, false, false, false),
                new ApoEndpoint(Cable16, false, false, false),
                new ApoEndpoint(Speaker, false, true, false),
                new ApoEndpoint(CableA, false, onCableA, false),
            },
        };

    /// <summary>A fresh copy of the owner's settings (the tests may change it).</summary>
    public static MixerSettings Settings() => JsonSerializer.Deserialize<MixerSettings>(Json)!;

    private const string Json = """
    {"Apps":[
      {"Key":"c:\\apps\\helium\\application\\chrome.exe","Name":"Helium","ExePath":"C:\\Apps\\Helium\\Application\\chrome.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":true,
       "Fx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[0],"Bands":[{"Id":1,"Enabled":true,"Freq":100,"Gain":0,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":689.4614662333313,"Gain":-24,"Q":0.14318109280297076,"Type":1},{"Id":3,"Enabled":true,"Freq":2500,"Gain":0,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}]},
      {"Key":"c:\\program files\\windowsapps\\anthropicpbc.claude_x64__fnn82j28hfe8t\\app\\claude.exe","Name":"Claude","ExePath":"C:\\Program Files\\WindowsApps\\AnthropicPBC.Claude_5.1231.2.0_x64__fnn82j28hfe8t\\app\\claude.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":false,
       "Fx":[{"Kind":"limiter","Name":"Limiter","Enabled":true,"Wet":100,"Values":[-1,100,0],"Bands":null,"Path":"","State":"","Custom":null}]},
      {"Key":"c:\\apps\\spotify\\spotify.exe","Name":"Spotify","ExePath":"C:\\Apps\\Spotify\\Spotify.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":true,
       "Fx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[0],"Bands":[{"Id":1,"Enabled":true,"Freq":100,"Gain":0,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":1059.275371930598,"Gain":24,"Q":0.36245329408663846,"Type":1},{"Id":3,"Enabled":true,"Freq":2500,"Gain":0,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}]}],
     "MasterFxOn":true,
     "MasterFx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[4],"Bands":[{"Id":1,"Enabled":true,"Freq":217.48216434353276,"Gain":10.5,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":479.2135416248817,"Gain":0,"Q":1,"Type":1},{"Id":3,"Enabled":true,"Freq":2491.675942207027,"Gain":-16,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}],
     "SoundMode":"full","MasterPlaysOn":[],
     "RealMasterDevice":"{0.0.0.00000000}.{35486a40-aa4f-4ae2-814d-37fb8824aaa2}",
     "BusCable":"{0.0.0.00000000}.{10207fab-95bc-4b24-9c2d-d29fbf19f212}",
     "Combined":[],"CaptureSilence":"",
     "Inputs":[{"Id":"{0.0.1.00000000}.{8d583f4c-56c5-4347-a56d-cc27105df83b}","Name":"Microphone Array (AMD Audio Device)","FxOn":false,"Fx":[],"Cable":""}]}
    """;
}
