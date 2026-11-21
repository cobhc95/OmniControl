using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// The wire format between paired PCs, in one place: the ports, the first byte of a connection (what it wants), the one message shape and
/// the items a PC shares (the basic set: what owners flagged <c>Common</c>). Everything after a connection proved who it is travels encrypted (<see cref="PcLink"/>); the beacon is the only
/// thing sent in clear and carries no more than the PC's id, name, port and whether pairing is open.
internal static class PcProtocol
{
    public const int TcpPort = 47810;
    public const int UdpPort = 47811;
    /// The protocol's one version (pairing, session proof and messages); both PCs need the same one.
    public const int Version = 3;

    /// The version is in the tag: PCs on another version neither see nor are seen.
    public static readonly string BeaconTag = $"omnicontrol-pc/{Version}";

    /// First byte of a connection: a pairing (only answered while pairing is open), a session of an already paired PC, or a probe (the same proof
    /// as a session, then one sealed ping answered with a pong, and the connection ends). Session and probe bytes change with the version, so a
    /// PC on another version is dropped at once instead of after a step's wait.
    public const byte WantsPairing = 0x50, WantsSession = 0x54, WantsProbe = 0x52;

    /// The largest message accepted: before a PC proved who it is, <see cref="MaxOpenFrame"/>; after, <see cref="MaxFrame"/>.
    public const int MaxOpenFrame = 4096, MaxFrame = 1 << 20;

    /// The id length in bytes (a random 128-bit number) and the length of every nonce and key.
    public const int IdBytes = 16, KeyBytes = 32;

    /// The id prefix of an item that belongs to another PC in this PC's master lists: <c>pc:&lt;pc id&gt;:&lt;its own id&gt;</c>.
    public const string RemotePrefix = "pc:";

    public static string RemoteId(string pcId, string itemId) => $"{RemotePrefix}{pcId}:{itemId}";

    public static string RemotePrefixOf(string pcId) => $"{RemotePrefix}{pcId}:";

    /// True for an item that came from another PC: never shared onwards, so two PCs can never bounce an item between them.
    public static bool IsRemote(string id) => id.StartsWith(RemotePrefix, StringComparison.Ordinal);

    public static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// The longest text taken from another PC for a name or value, unless a shorter limit is given.
    public const int TextMax = 120;

    /// Text another PC sent, without control or format characters (bidirectional overrides), trimmed and cut to <paramref name="max"/> characters.
    public static string Clip(string? text, int max = TextMax) => TextClip.Clean(text, max);

    /// "host" or "host:port" (the default port when none): false when it is not one.
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = string.Empty;
        port = TcpPort;
        text = text.Trim();
        if (text.Length == 0) return false;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon) // one colon: host:port (an IPv6 address has several and is taken whole)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            text = text[..colon];
        }
        host = text;
        return host.Length > 0;
    }
}

/// What a message is.
internal static class PcMsgType
{
    public const string Catalog = "catalog", State = "state", Set = "set", Run = "run", Ran = "ran", Ping = "ping", Pong = "pong",
        Devices = "devices", DevicesAnswer = "devices-answer", TailscaleAddress = "ts-address",
        WakeInfo = "wake-info", Leaving = "leaving", WakeAsk = "wake-ask", WakeAnswer = "wake-answer",
        ImportList = "import-list", ImportGet = "import-get", ImportAnswer = "import-answer", Route = "route", Countdown = "countdown";

    /// What a <c>wake-ask</c> wants (its <see cref="PcMsg.Id"/>): send a magic packet for the paired PC whose id is in <see cref="PcMsg.Text"/> (asked only for a person's press of Wake there; the
    /// older "relay", which retries and rules also sent, is refused), read the wake facts again, turn settings on or put back what was turned on (behind this PC's own
    /// prompt), or put this PC to sleep as a test (behind its prompt).
    public const string WakeRelay = "relay-pressed", WakeCheck = "check", WakeFix = "fix", WakeUndo = "undo", WakeTest = "test";
}

/// One state a PC shares: what a PC that listens needs to show it and to register it (its current value comes with it). <see cref="Group"/> is the
/// page it belongs to, <see cref="Heading"/> the part of the page ("Displays - LG 27"), <see cref="Icon"/> the master entry's library icon (never a
/// user icon: the other PC does not have it), <see cref="Settable"/> whether the other PC may set it (the owner gave the entry a way to).
public sealed record SharedState(string Id, string Name, string Group, int Kind, int Min, int Max, string Unit, List<string>? Choices, double? Number, string? Text,
    string? Heading = null, string? Icon = null, bool Settable = false, bool ScreenPower = false, string? Common = null, string? Subject = null);

/// One action a PC shares and lets the other run: its title, its icon, the card (<see cref="Common"/>) it belongs to, whether it turns every screen off, whether the other PC must confirm it first
/// and the seconds of the power countdown it runs with (null: none, or a PC on an older build).
public sealed record SharedAction(string Id, string Title, string? Icon, string Common, bool ScreensOff = false, bool ConfirmRun = false, int? Countdown = null);

/// The one message shape (only the fields its <see cref="T"/> uses are set). <c>catalog</c>: everything shared with the receiver now;
/// <c>state</c>: one state's new value; <c>set</c>: set the shared state <see cref="Id"/> to <see cref="Number"/> / <see cref="Text"/> (answered with <c>ran</c>);
/// <c>ts-address</c>: the sender's Tailscale address in <see cref="Text"/>, so the receiver can dial it once Tailscale works between the two;
/// <c>run</c>: start the action <see cref="Id"/> (<see cref="Rid"/> numbers the request, <see cref="Chain"/>
/// is the chain of rules that caused it); <c>ran</c>: its answer; <c>route</c>: the route the sender now uses for this pair (<see cref="PcRoute"/> name in
/// <see cref="Text"/>), which the receiver takes too; <c>countdown</c>: the sender's power countdown of the action <see cref="Id"/> started or ran out
/// (<see cref="LeftMs"/>, the time left, so the two clocks never need to agree) or ended (both null). A PC on an older build ignores it.
internal sealed class PcMsg
{
    public string T { get; set; } = string.Empty;
    public string? Id { get; set; }
    public double? Number { get; set; }
    public string? Text { get; set; }

    /// <c>state</c>: the device or thing the value is about (<see cref="StateValue.Subject"/>).
    public string? Subject { get; set; }
    public int? Rid { get; set; }
    public bool? Ok { get; set; }
    public string? Why { get; set; }
    public List<string>? Chain { get; set; }

    /// <c>countdown</c>: the milliseconds left (zero: it ran out and the action is being done).
    public int? LeftMs { get; set; }

    /// <c>devices</c>: the request includes the user's own script templates. <c>devices-answer</c>: the device set-ups as text (what <see cref="Payload"/> holds is not this layer's business).
    public bool? Scripts { get; set; }

    /// <c>wake-info</c> and <c>wake-answer</c>: how this PC can be woken. <c>leaving</c>: <see cref="Text"/> is "sleep", "hibernate" or "shutdown". <c>wake-ask</c>: <see cref="Id"/> is what is wanted (<see cref="PcMsgType.WakeRelay"/>...), <see cref="Text"/> the PC to wake (its id) or the fixes.
    public WakeFacts? Wake { get; set; }
    public string? Payload { get; set; }
    public List<SharedState>? States { get; set; }
    public List<SharedAction>? Actions { get; set; }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, PcProtocol.Json);

    /// The message in these bytes, or null when they are not one (an unreadable message ends the link).
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

/// What a PC says about itself on the local network.
internal sealed record PcBeacon(string M, string Id, string Name, int Port, bool Pairing);
