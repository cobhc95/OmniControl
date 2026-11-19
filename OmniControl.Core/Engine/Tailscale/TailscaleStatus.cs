using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace OmniControl.Core.Engine.Tailscale;

/// Where Tailscale's own program says it stands.
public enum TailscaleBackend { Unknown, Stopped, NeedsLogin, Starting, Running }

/// A PC of the tailnet (a Windows one is what Other PCs can pair with).
public sealed record TailscalePeer(string Name, string Address, string Os, bool Online);

/// What one read of Tailscale found. Nothing here changes anything.
public sealed record TailscaleStatus(string? CliPath, bool Answered, TailscaleBackend Backend, string Version, string Account, string OwnAddress, IReadOnlyList<TailscalePeer> Peers)
{
    public static readonly TailscaleStatus NotInstalled = new(null, false, TailscaleBackend.Unknown, string.Empty, string.Empty, string.Empty, Array.Empty<TailscalePeer>());

    public bool Installed => CliPath is not null;

    /// Tailscale's program answered and is not switched off.
    public bool Running => Answered && Backend != TailscaleBackend.Stopped;

    public bool SignedIn => Answered && Backend != TailscaleBackend.NeedsLogin;

    public bool Connected => Answered && Backend == TailscaleBackend.Running && OwnAddress.Length > 0;
}

/// Reads <c>tailscale status --json</c> (the one read-only call) into a <see cref="TailscaleStatus"/>.
public static class TailscaleStatusReader
{
    private const string WindowsOs = "windows";

    /// Reads Tailscale's state now. Call off the UI thread.
    public static TailscaleStatus Read()
    {
        if (TailscaleCli.Find() is not { } cli) return TailscaleStatus.NotInstalled;
        var output = TailscaleCli.Run(cli, "status --json");
        return output is null ? TailscaleStatus.NotInstalled with { CliPath = cli } : Parse(cli, output);
    }

    /// The status JSON as a <see cref="TailscaleStatus"/>; text that is not that gives "not answering".
    public static TailscaleStatus Parse(string cliPath, string json)
    {
        var none = TailscaleStatus.NotInstalled with { CliPath = cliPath };
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return none;
            var self = root.TryGetProperty("Self", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            var own = self.ValueKind == JsonValueKind.Object ? FirstIPv4(self) : string.Empty;
            return new TailscaleStatus(cliPath, true, BackendOf(Text(root, "BackendState")), VersionOf(Text(root, "Version")), AccountOf(root, self), own, PeersOf(root));
        }
        catch (JsonException)
        {
            return none;
        }
    }

    /// True for an address Tailscale hands out (100.64.0.0/10 or its IPv6 range): the only kind a pairing may store to dial.
    public static bool IsTailscaleAddress(string? text)
    {
        if (!IPAddress.TryParse(text, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => bytes[0] == 100 && (bytes[1] & 0xC0) == 0x40,
            AddressFamily.InterNetworkV6 => bytes[0] == 0xfd && bytes[1] == 0x7a && bytes[2] == 0x11 && bytes[3] == 0x5c && bytes[4] == 0xa1 && bytes[5] == 0xe0,
            _ => false,
        };
    }

    private static TailscaleBackend BackendOf(string state) => state switch
    {
        "Running" => TailscaleBackend.Running,
        "Stopped" => TailscaleBackend.Stopped,
        "NeedsLogin" => TailscaleBackend.NeedsLogin,
        "Starting" => TailscaleBackend.Starting,
        _ => TailscaleBackend.Unknown,
    };

    private static string VersionOf(string raw) => raw.Split('-')[0];

    private static string AccountOf(JsonElement root, JsonElement self)
    {
        if (self.ValueKind != JsonValueKind.Object || !self.TryGetProperty("UserID", out var id)
            || !root.TryGetProperty("User", out var users) || users.ValueKind != JsonValueKind.Object) return string.Empty;
        return users.TryGetProperty(id.ToString(), out var user) && user.ValueKind == JsonValueKind.Object ? Text(user, "LoginName") : string.Empty;
    }

    private static IReadOnlyList<TailscalePeer> PeersOf(JsonElement root)
    {
        var peers = new List<TailscalePeer>();
        if (!root.TryGetProperty("Peer", out var all) || all.ValueKind != JsonValueKind.Object) return peers;
        foreach (var entry in all.EnumerateObject())
        {
            var peer = entry.Value;
            if (peer.ValueKind != JsonValueKind.Object) continue;
            var address = FirstIPv4(peer);
            if (address.Length == 0) continue;
            var name = Text(peer, "HostName");
            peers.Add(new TailscalePeer(name.Length > 0 ? name : address, address, Text(peer, "OS").ToLowerInvariant(), peer.TryGetProperty("Online", out var on) && on.ValueKind == JsonValueKind.True));
        }
        return peers.Where(p => p.Os == WindowsOs).OrderByDescending(p => p.Online).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FirstIPv4(JsonElement node)
    {
        if (!node.TryGetProperty("TailscaleIPs", out var ips) || ips.ValueKind != JsonValueKind.Array) return string.Empty;
        return ips.EnumerateArray().Select(i => i.GetString() ?? string.Empty).FirstOrDefault(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) ?? string.Empty;
    }

    private static string Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
