using OmniControl.Core.Engine.Apo;

namespace OmniControl.Tests;

/// A reference PC (made-up endpoint ids and program paths): one VB-CABLE with two inputs ("CABLE Input" and
/// "CABLE In 16ch") and one output, a Realtek speaker, the master bus, a browser and a player with an equaliser each, a chat app
/// with effects off, the master with an equaliser.
internal static class OwnerPc
{
    public const string CableInput = "{0.0.0.00000000}.{11111111-0000-4000-8000-000000000001}";
    public const string Cable16 = "{0.0.0.00000000}.{11111111-0000-4000-8000-000000000002}";
    public const string Speaker = "{0.0.0.00000000}.{11111111-0000-4000-8000-000000000003}";
    public const string CableOutput = "{0.0.1.00000000}.{22222222-0000-4000-8000-000000000001}";
    public const string StereoMix = "{0.0.1.00000000}.{22222222-0000-4000-8000-000000000002}";
    public const string MicArray = "{0.0.1.00000000}.{22222222-0000-4000-8000-000000000003}";

    public const string Browser = @"c:\apps\browser\browser.exe";
    public const string Chat = @"c:\program files\windowsapps\examplepublisher.chat_x64__abcd1234efgh5\app\chat.exe";
    public const string Player = @"c:\apps\player\player.exe";

    /// Windows' default is the bus cable, as it is while the master bus runs.
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

    /// What the probe reports for this PC (the cables found by the same code the app uses).
    public static AudioEffectsSupport Support { get; } =
        new(true, VirtualCables.Find(Playback, Recording), ApoState.None, CableInput);

    public const string CableA = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-00000000000a}";
    public const string CableAOutput = "{0.0.1.00000000}.{aaaaaaaa-0000-0000-0000-00000000000b}";

    /// The same PC with a second, independent cable pair (VB-CABLE A): what a processed program needs.
    public static IReadOnlyList<AudioDevice> PlaybackTwoPairs { get; } =
        Playback.Append(new AudioDevice(CableA, "CABLE-A Input (VB-Audio Cable A)", false)).ToArray();

    public static IReadOnlyList<AudioDevice> RecordingTwoPairs { get; } =
        Recording.Append(new AudioDevice(CableAOutput, "CABLE-A Output (VB-Audio Cable A)", false)).ToArray();

    public static AudioEffectsSupport SupportTwoPairs { get; } =
        new(true, VirtualCables.Find(PlaybackTwoPairs, RecordingTwoPairs), ApoState.None, CableInput);

    /// The same PC once the speakers are Windows' default again (the master in Equalizer APO: no bus owns the default).
    public static IReadOnlyList<AudioDevice> PlaybackSpeakerDefault { get; } =
        Playback.Select(d => d with { IsDefault = d.Id == Speaker }).ToArray();

    public static IReadOnlyList<AudioDevice> PlaybackTwoPairsSpeakerDefault { get; } =
        PlaybackTwoPairs.Select(d => d with { IsDefault = d.Id == Speaker }).ToArray();

    /// Equalizer APO's config folder in the made-up APO states (nothing is ever written there: the tests use a fake store).
    public const string ApoFolder = @"C:\Program Files\EqualizerAPO\config";

    /// Equalizer APO installed, registered and loaded, ticked on the Realtek speaker (the one-time Device Selector step) and, when
    /// <paramref name="onCableA"/>, on "CABLE-A Input" too. Windows' default as given.
    public static ApoState ApoOnSpeaker(bool onCableA = false, string defaultId = Speaker) =>
        new(true, true, true, ApoFolder, null, new ApoEndpoint(defaultId, defaultId == Speaker, false), Array.Empty<ApoEndpoint>())
        {
            Playback = new[]
            {
                new ApoEndpoint(CableInput, false, false),
                new ApoEndpoint(Cable16, false, false),
                new ApoEndpoint(Speaker, true, false),
                new ApoEndpoint(CableA, onCableA, false),
            },
        };

    /// A fresh copy of the settings (the tests may change it).
    /// The reference settings with "Use microphones" on (the microphone plans need it).
    public static MixerSettings Settings()
    {
        var s = JsonSerializer.Deserialize<MixerSettings>(Json)!;
        s.UseMicrophones = true;
        MixerSettings.UpgradeMethods(s); // as loading a file from before Off does: rows with effects on Automatic
        return s;
    }

    private const string Json = """
    {"Apps":[
      {"Key":"c:\\apps\\browser\\browser.exe","Name":"Browser","ExePath":"C:\\Apps\\Browser\\Browser.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":true,
       "Fx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[0],"Bands":[{"Id":1,"Enabled":true,"Freq":100,"Gain":0,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":689.4614662333313,"Gain":-24,"Q":0.14318109280297076,"Type":1},{"Id":3,"Enabled":true,"Freq":2500,"Gain":0,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}]},
      {"Key":"c:\\program files\\windowsapps\\examplepublisher.chat_x64__abcd1234efgh5\\app\\chat.exe","Name":"Chat","ExePath":"C:\\Program Files\\WindowsApps\\ExamplePublisher.Chat_1.2.3.0_x64__abcd1234efgh5\\app\\chat.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":false,
       "Fx":[{"Kind":"limiter","Name":"Limiter","Enabled":true,"Wet":100,"Values":[-1,100,0],"Bands":null,"Path":"","State":"","Custom":null}]},
      {"Key":"c:\\apps\\player\\player.exe","Name":"Player","ExePath":"C:\\Apps\\Player\\Player.exe","Volume":100,"Pan":0,"Muted":false,"PlaysOn":[],"FxOn":true,
       "Fx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[0],"Bands":[{"Id":1,"Enabled":true,"Freq":100,"Gain":0,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":1059.275371930598,"Gain":24,"Q":0.36245329408663846,"Type":1},{"Id":3,"Enabled":true,"Freq":2500,"Gain":0,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}]}],
     "MasterFxOn":true,
     "MasterFx":[{"Kind":"eq","Name":"Equaliser - Parametric","Enabled":true,"Wet":100,"Values":[4],"Bands":[{"Id":1,"Enabled":true,"Freq":217.48216434353276,"Gain":10.5,"Q":0.707,"Type":0},{"Id":2,"Enabled":true,"Freq":479.2135416248817,"Gain":0,"Q":1,"Type":1},{"Id":3,"Enabled":true,"Freq":2491.675942207027,"Gain":-16,"Q":1,"Type":1},{"Id":4,"Enabled":true,"Freq":8000,"Gain":0,"Q":0.707,"Type":2}],"Path":"","State":"","Custom":null}],
     "SoundMode":"full","MasterPlaysOn":[],
     "RealMasterDevice":"{0.0.0.00000000}.{11111111-0000-4000-8000-000000000003}",
     "BusCable":"{0.0.0.00000000}.{11111111-0000-4000-8000-000000000001}",
     "Combined":[],"CaptureSilence":"",
     "Inputs":[{"Id":"{0.0.1.00000000}.{22222222-0000-4000-8000-000000000003}","Name":"Microphone Array (AMD Audio Device)","FxOn":false,"Fx":[],"Cable":""}]}
    """;
}
