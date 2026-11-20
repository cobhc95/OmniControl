using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniControl.Core.Engine.Resources;

/// The network service's fixed requests; nothing else is ever done for a client. RefreshAdapters: Windows' adapters may have changed (the
/// app's hardware watch says so), so running download limits pick up a new one.
public enum NetOp { Status, ReadSpeeds, SetUpload, ClearUpload, SetDownload, ClearDownload, Block, Unblock, ListRules, RefreshAdapters }

/// One request: the protocol version, what is asked, the program's path (path requests only) and the limit in KB/s (Set requests only).
public sealed record NetRequest(int V, NetOp Op, string? Path = null, int Kbps = 0);

/// What the service holds for one program, limits in KB/s (0: none).
public sealed record NetRule(string Path, int UploadKbps = 0, int DownloadKbps = 0, bool Blocked = false)
{
    [JsonIgnore] public bool IsEmpty => UploadKbps == 0 && DownloadKbps == 0 && !Blocked;

    public bool SameLimits(NetRule other) => UploadKbps == other.UploadKbps && DownloadKbps == other.DownloadKbps && Blocked == other.Blocked;

    /// A wanted rule as the service is asked for it (the rates were made from whole KB/s).
    public static NetRule From(NetworkRule rule) =>
        new(rule.Path, (int)((rule.UploadBits ?? 0) / BitsPerKb), (int)((rule.DownloadBytes ?? 0) / BytesPerKb), rule.Blocked);

    /// The rule the service gives its work; the program's identity is its file name, as everywhere.
    public NetworkRule ToRule() => new(ProgramIdentity.Normalize(Path), Path, UploadKbps > 0 ? (ulong)UploadKbps * BitsPerKb : null, Blocked,
        DownloadKbps > 0 ? (long)DownloadKbps * BytesPerKb : null);

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
    public const int Version = 1;
    public const string Name = "OmniControl.NetworkService";

    /// The largest message each way, the longest path, the highest limit (1 GB/s), how many programs the service holds and how many
    /// programs' speeds and problems one reply carries at most.
    public const int MaxRequest = 4 * 1024, MaxReply = 512 * 1024, MaxPath = 1024, MaxKbps = 1024 * 1024, MaxRules = 256, MaxSpeeds = 256, MaxProblems = 32;

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
            NetOp.Block => (true, false, true),
            _ => (true, false, false), // a clear acts only on a path the service holds
        };
        if (rate ? request.Kbps is < 1 or > MaxKbps : request.Kbps != 0) return (null, $"the limit must be {(rate ? $"1 to {MaxKbps} KB/s" : "0")}");
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

    private static readonly char[] Forbidden = { ':', '*', '?', '"', '<', '>', '|', '/' };

    /// The requests that take one program from what the service holds (<paramref name="held"/>) to <paramref name="wanted"/> (null: nothing).
    public static IEnumerable<NetRequest> Changes(string path, NetRule? held, NetRule? wanted)
    {
        var (from, to) = (held ?? new NetRule(path), wanted ?? new NetRule(path));
        if (from.UploadKbps != to.UploadKbps) yield return to.UploadKbps > 0 ? new(Version, NetOp.SetUpload, path, to.UploadKbps) : new(Version, NetOp.ClearUpload, path);
        if (from.DownloadKbps != to.DownloadKbps) yield return to.DownloadKbps > 0 ? new(Version, NetOp.SetDownload, path, to.DownloadKbps) : new(Version, NetOp.ClearDownload, path);
        if (from.Blocked != to.Blocked) yield return new(Version, to.Blocked ? NetOp.Block : NetOp.Unblock, path);
    }
}
