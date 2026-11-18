using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>
/// The wire format between paired PCs, in one place: the ports, the first byte of a connection (what it wants), the one message shape and
/// the items a PC shares (the basic set: what owners flagged <c>Common</c>). Everything after a connection proved who it is travels encrypted (<see cref="PcLink"/>); the beacon is the only
/// thing sent in clear and carries no more than the PC's id, name, port and whether pairing is open.
/// </summary>
internal static class PcProtocol
{
    public const int TcpPort = 47810;
    public const int UdpPort = 47811;
    public const string BeaconTag = "omnicontrol-pc/1";

    /// <summary>First byte of a connection: a pairing (only answered while pairing is open), a session of an already paired PC, or a probe (the same proof as a session, then one sealed ping answered with a pong, and the connection ends).</summary>
    public const byte WantsPairing = 0x50, WantsSession = 0x53, WantsProbe = 0x51;

    /// <summary>The largest message accepted: before a PC proved who it is, <see cref="MaxOpenFrame"/>; after, <see cref="MaxFrame"/>.</summary>
    public const int MaxOpenFrame = 4096, MaxFrame = 1 << 20;

    /// <summary>The id length in bytes (a random 128-bit number) and the length of every nonce and key.</summary>
    public const int IdBytes = 16, KeyBytes = 32;

    /// <summary>The id prefix of an item that belongs to another PC in this PC's master lists: <c>pc:&lt;pc id&gt;:&lt;its own id&gt;</c>.</summary>
    public const string RemotePrefix = "pc:";

    public static string RemoteId(string pcId, string itemId) => $"{RemotePrefix}{pcId}:{itemId}";

    public static string RemotePrefixOf(string pcId) => $"{RemotePrefix}{pcId}:";

    /// <summary>True for an item that came from another PC: never shared onwards, so two PCs can never bounce an item between them.</summary>
    public static bool IsRemote(string id) => id.StartsWith(RemotePrefix, StringComparison.Ordinal);

    public static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>What a message is.</summary>
internal static class PcMsgType
{
    public const string Catalog = "catalog", State = "state", Set = "set", Run = "run", Ran = "ran", Ping = "ping", Pong = "pong",
        Devices = "devices", DevicesAnswer = "devices-answer", TailscaleAddress = "ts-address",
        WakeInfo = "wake-info", Leaving = "leaving", WakeAsk = "wake-ask", WakeAnswer = "wake-answer",
        ImportList = "import-list", ImportGet = "import-get", ImportAnswer = "import-answer", Route = "route";

    /// <summary>What a <c>wake-ask</c> wants (its <see cref="PcMsg.Id"/>): send a magic packet for a PC on this PC's network, read the wake facts again, turn settings on (behind this PC's own prompt), or put this PC to sleep as a test (behind its prompt).</summary>
    public const string WakeRelay = "relay", WakeCheck = "check", WakeFix = "fix", WakeTest = "test";
}

/// <summary>
/// One state a PC shares: what a PC that listens needs to show it and to register it (its current value comes with it). <see cref="Group"/> is the
/// page it belongs to, <see cref="Heading"/> the part of the page ("Displays - LG 27"), <see cref="Icon"/> the master entry's library icon (never a
/// user icon: the other PC does not have it), <see cref="Settable"/> whether the other PC may set it (the owner gave the entry a way to).
/// </summary>
public sealed record SharedState(string Id, string Name, string Group, int Kind, int Min, int Max, string Unit, List<string>? Choices, double? Number, string? Text,
    string? Heading = null, string? Icon = null, bool Settable = false, bool ScreenPower = false, string? Common = null);

/// <summary>One action a PC shares and lets the other run: its title, its icon, the card (<see cref="Common"/>) it belongs to, whether it turns every screen off and whether the other PC must confirm it first.</summary>
public sealed record SharedAction(string Id, string Title, string? Icon, string Common, bool ScreensOff = false, bool ConfirmRun = false);

/// <summary>
/// The one message shape (only the fields its <see cref="T"/> uses are set). <c>catalog</c>: everything shared with the receiver now;
/// <c>state</c>: one state's new value; <c>set</c>: set the shared state <see cref="Id"/> to <see cref="Number"/> / <see cref="Text"/> (answered with <c>ran</c>);
/// <c>ts-address</c>: the sender's Tailscale address in <see cref="Text"/>, so the receiver can dial it once Tailscale works between the two;
/// <c>run</c>: start the action <see cref="Id"/> (<see cref="Rid"/> numbers the request, <see cref="Chain"/>
/// is the chain of rules that caused it); <c>ran</c>: its answer; <c>route</c>: the route the sender now uses for this pair (<see cref="PcRoute"/> name in
/// <see cref="Text"/>), which the receiver takes too.
/// </summary>
internal sealed class PcMsg
{
    public string T { get; set; } = string.Empty;
    public string? Id { get; set; }
    public double? Number { get; set; }
    public string? Text { get; set; }
    public int? Rid { get; set; }
    public bool? Ok { get; set; }
    public string? Why { get; set; }
    public List<string>? Chain { get; set; }

    /// <summary><c>devices</c>: the request includes the user's own script templates. <c>devices-answer</c>: the device set-ups as text (what <see cref="Payload"/> holds is not this layer's business).</summary>
    public bool? Scripts { get; set; }

    /// <summary><c>wake-info</c> and <c>wake-answer</c>: how this PC can be woken. <c>leaving</c>: <see cref="Text"/> is "sleep", "hibernate" or "shutdown". <c>wake-ask</c>: <see cref="Id"/> is what is wanted (<see cref="PcMsgType.WakeRelay"/>...), <see cref="Text"/> its address or the fixes, <see cref="Payload"/> the network.</summary>
    public WakeFacts? Wake { get; set; }
    public string? Payload { get; set; }
    public List<SharedState>? States { get; set; }
    public List<SharedAction>? Actions { get; set; }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, PcProtocol.Json);

    /// <summary>The message in these bytes, or null when they are not one (an unreadable message ends the link).</summary>
    public static PcMsg? From(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<PcMsg>(bytes, PcProtocol.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What a PC says about itself on the local network.</summary>
internal sealed record PcBeacon(string M, string Id, string Name, int Port, bool Pairing);
