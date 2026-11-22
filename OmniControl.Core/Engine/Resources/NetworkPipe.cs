using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniControl.Core.Engine.Resources;

/// The network service's fixed requests; nothing else is ever done for a client. RefreshAdapters: Windows' adapters may have changed (the
/// app's hardware watch says so), so running download limits pick up a new one.
public enum NetOp { Status, ReadSpeeds, SetUpload, ClearUpload, SetDownload, ClearDownload, Block, Unblock, BlockUpload, UnblockUpload, BlockDownload, UnblockDownload, ListRules, RefreshAdapters }

/// One request: the protocol version, what is asked, the program's path (path requests only), the limit in KB/s (Set requests only) and
/// where it applies: one adapter (its id and name; blocks and download limits) or networks of one category (<see cref="NetworkRule.QosProfile"/>; upload limits).
public sealed record NetRequest(int V, NetOp Op, string? Path = null, int Kbps = 0, string? Adapter = null, string? AdapterName = null, int Profile = 0);

/// What the service holds for one program in one place (<see cref="Adapter"/>, <see cref="Profile"/>; none: everywhere), limits in KB/s
/// (0: none); <see cref="Blocked"/> both ways, or one way only.
public sealed record NetRule(string Path, int UploadKbps = 0, int DownloadKbps = 0, bool Blocked = false, bool UploadBlocked = false, bool DownloadBlocked = false,
    string? Adapter = null, string? AdapterName = null, int Profile = 0)
{
    /// One program's rule in one place: what the service and its client hold rules by.
    [JsonIgnore] public string Key => $"{Path.ToUpperInvariant()}|{Adapter}|{Profile}";

    [JsonIgnore] public bool IsEmpty => UploadKbps == 0 && DownloadKbps == 0 && !Blocked && !UploadBlocked && !DownloadBlocked;

    public bool SameLimits(NetRule other) => this with { Path = string.Empty, AdapterName = null } == other with { Path = string.Empty, AdapterName = null };

    /// A wanted rule as the service is asked for it (the rates were made from whole KB/s).
    public static NetRule From(NetworkRule rule) =>
        new(rule.Path, (int)((rule.UploadBits ?? 0) / BitsPerKb), (int)((rule.DownloadBytes ?? 0) / BytesPerKb), rule.Blocked, rule.UploadBlocked, rule.DownloadBlocked,
            rule.Adapter, rule.AdapterName, rule.QosProfile);

    /// The rule the service gives its work; the program's identity is its file name, as everywhere.
    public NetworkRule ToRule() => new(ProgramIdentity.Normalize(Path), Path, UploadKbps > 0 ? (ulong)UploadKbps * BitsPerKb : null, Blocked,
        DownloadKbps > 0 ? (long)DownloadKbps * BytesPerKb : null, UploadBlocked, DownloadBlocked, Adapter, AdapterName, NetworkRule.ProfileOf(Profile));

    private const int BytesPerKb = 1024, BitsPerKb = BytesPerKb * 8;
}

/// The service's answer: its version, a refusal (one sentence) or the problems of the sync the request caused, why download shaping ended
/// (null while it runs or is not asked), and the rules (ListRules) or the traffic since the last read (ReadSpeeds).
public sealed record NetReply(int V, string? Error = null, IReadOnlyList<string>? Problems = null, string? DownloadFailure = null,
    IReadOnlyList<NetRule>? Rules = null, IReadOnlyList<AppTraffic>? Speeds = null, string? SpeedsStopped = null);

/// The network service's protocol: small JSON messages, each after its length (4 bytes, little-endian), over a byte-mode named pipe.
/// Every request is checked before anything runs (<see cref="Parse"/>): version, a known request, no unknown field, the path and the limit.
public static class NetworkPipe
{
    public const int Version = 3;
    public const string Name = "OmniControl.NetworkService";

    /// The largest message each way, the longest path, the highest limit (1 GB/s), how many programs the service holds and how many
    /// programs' speeds and problems one reply carries at most.
    public const int MaxRequest = 4 * 1024, MaxReply = 512 * 1024, MaxPath = 1024, MaxKbps = 1024 * 1024, MaxRules = 256, MaxSpeeds = 256, MaxProblems = 32,
        MaxAdapterName = 256;

    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<NetOp>(allowIntegerValues: false) },
    };

    public static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

    /// The reply in <paramref name="body"/>, or null when it is not one.
    public static NetReply? ReadReply(byte[] body)
    {
        try { return JsonSerializer.Deserialize<NetReply>(body, Json); }
        catch (JsonException) { return null; }
    }

    /// Writes one message; throws <see cref="InvalidDataException"/> when it is larger than <paramref name="max"/>.
    public static async Task WriteAsync(Stream stream, byte[] body, int max, CancellationToken token)
    {
        if (body.Length > max) throw new InvalidDataException($"A message of {body.Length} bytes is larger than {max}.");
        var message = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message, body.Length);
        body.CopyTo(message, 4);
        await stream.WriteAsync(message, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// Reads one message: null when the other side closed before one began; throws <see cref="InvalidDataException"/> for an empty or too
    /// large one, <see cref="EndOfStreamException"/> for one cut short.
    public static async Task<byte[]?> ReadAsync(Stream stream, int max, CancellationToken token)
    {
        var head = new byte[4];
        var first = await stream.ReadAsync(head.AsMemory(0, 4), token).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(head.AsMemory(first), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (length <= 0 || length > max) throw new InvalidDataException($"A message of {length} bytes is not allowed (at most {max}).");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
        return body;
    }

    /// Checks one request: the request with its path made whole (<see cref="Path.GetFullPath(string)"/>), or why it is refused.
    /// <param name="isProgram">Whether a program file is at the path (asked only for requests that put a rule into Windows).</param>
    /// <param name="windowsFolder">Programs in it are refused: Windows' own programs are never limited or blocked through the service.</param>
    public static (NetRequest? Request, string? Refused) Parse(byte[] body, Func<string, bool> isProgram, string windowsFolder)
    {
        NetRequest? request;
        try { request = JsonSerializer.Deserialize<NetRequest>(body, Json); }
        catch (JsonException) { return (null, "not a request this service knows"); }
        if (request is null) return (null, "not a request this service knows");
        if (request.V != Version) return (null, $"protocol version {request.V} is not {Version}");
        var (path, rate, mustExist) = request.Op switch
        {
            NetOp.Status or NetOp.ReadSpeeds or NetOp.ListRules or NetOp.RefreshAdapters => (false, false, false),
            NetOp.SetUpload or NetOp.SetDownload => (true, true, true),
            NetOp.Block or NetOp.BlockUpload or NetOp.BlockDownload => (true, false, true),
            _ => (true, false, false), // a clear acts only on a path the service holds
        };
        if (rate ? request.Kbps is < 1 or > MaxKbps : request.Kbps != 0) return (null, $"the limit must be {(rate ? $"1 to {MaxKbps} KB/s" : "0")}");
        if (ScopeRefused(request) is { } scope) return (null, scope);
        if (!path) return request.Path is null ? (request, null) : (null, "this request takes no path");
        if (WholePath(request.Path, windowsFolder) is not { } whole) return (null, "the path is not a program's full local path");
        if (mustExist && !isProgram(whole)) return (null, "no program is at that path");
        return (request with { Path = whole }, null);
    }

    /// A drive-letter path to an .exe, at most <see cref="MaxPath"/> long, with no control, wildcard or stream characters and outside the
    /// Windows folder, made whole; null otherwise (UNC and device paths included).
    internal static string? WholePath(string? path, string windowsFolder)
    {
        if (path is null || path.Length is < 4 or > MaxPath || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') return null;
        if (path.AsSpan(2).IndexOfAny(Forbidden) >= 0 || path.Any(char.IsControl)) return null;
        string whole;
        try { whole = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException) { return null; }
        var windows = Path.TrimEndingDirectorySeparator(windowsFolder) + Path.DirectorySeparatorChar;
        return whole.Length <= MaxPath && whole.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !whole.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ? whole : null;
    }

    /// Why a request's place is refused, or null: an adapter is a "{GUID}" with a short plain name, for a path request other than an upload
    /// limit; a network category (1 to 3) only for an upload limit.
    private static string? ScopeRefused(NetRequest request)
    {
        var upload = request.Op is NetOp.SetUpload or NetOp.ClearUpload;
        if (request.Profile is < 0 or > 3 || (request.Profile != 0 && !upload)) return "a network category is only for an upload limit";
        if (request.Adapter is null) return request.AdapterName is null ? null : "an adapter name needs its adapter";
        if (upload || request.Path is null) return "an adapter is only for a block or a download limit";
        if (request.Adapter.Length != 38 || !Guid.TryParseExact(request.Adapter, "B", out _)) return "the adapter is not an adapter id";
        return request.AdapterName is { Length: > 0 and <= MaxAdapterName } name && !name.Any(char.IsControl) ? null : "the adapter's name is not a plain name";
    }

    private static readonly char[] Forbidden = { ':', '*', '?', '"', '<', '>', '|', '/' };

    /// The requests that take one program in one place from what the service holds (<paramref name="held"/>) to <paramref name="wanted"/>
    /// (null: nothing); both name the same place.
    public static IEnumerable<NetRequest> Changes(string path, NetRule? held, NetRule? wanted)
    {
        var place = wanted ?? held ?? new NetRule(path);
        var (from, to) = (held ?? place with { UploadKbps = 0, DownloadKbps = 0, Blocked = false, UploadBlocked = false, DownloadBlocked = false },
            wanted ?? place with { UploadKbps = 0, DownloadKbps = 0, Blocked = false, UploadBlocked = false, DownloadBlocked = false });
        NetRequest Ask(NetOp op, int kbps = 0) => op is NetOp.SetUpload or NetOp.ClearUpload
            ? new(Version, op, path, kbps, Profile: place.Profile)
            : new(Version, op, path, kbps, place.Adapter, place.AdapterName);
        if (from.UploadKbps != to.UploadKbps) yield return to.UploadKbps > 0 ? Ask(NetOp.SetUpload, to.UploadKbps) : Ask(NetOp.ClearUpload);
        if (from.DownloadKbps != to.DownloadKbps) yield return to.DownloadKbps > 0 ? Ask(NetOp.SetDownload, to.DownloadKbps) : Ask(NetOp.ClearDownload);
        if (from.Blocked != to.Blocked) yield return Ask(to.Blocked ? NetOp.Block : NetOp.Unblock);
        if (from.UploadBlocked != to.UploadBlocked) yield return Ask(to.UploadBlocked ? NetOp.BlockUpload : NetOp.UnblockUpload);
        if (from.DownloadBlocked != to.DownloadBlocked) yield return Ask(to.DownloadBlocked ? NetOp.BlockDownload : NetOp.UnblockDownload);
    }
}
