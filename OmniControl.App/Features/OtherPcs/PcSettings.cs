using System.Text.Json.Serialization;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// How this PC reaches a paired PC: found on the local network (<see cref="Lan"/>), at an address typed for it (<see cref="Address"/>: ZeroTier or any other network that carries it), or at the Tailscale address both PCs gave each other when they paired (<see cref="Tailscale"/>).
public enum PcRoute { Lan, Address, Tailscale }

/// What a paired PC last shared (its basic set), kept so its entries stay listed, unavailable, while it is offline and after a restart.
public sealed class PcCatalog
{
    public List<SharedState> States { get; set; } = new();
    public List<SharedAction> Actions { get; set; } = new();
}

/// One paired PC as it is saved. Its pairing key is not here: Windows' credential store keeps it.
public sealed class PairedPc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PcRoute Route { get; set; } = PcRoute.Lan;

    /// A route chosen here that the other PC has not been told yet (it was unreachable); sent on the next contact. Null = nothing waiting.
    public PcRoute? PendingRoute { get; set; }

    /// The name the user gave this PC on this PC only (never sent to it); empty = none, <see cref="Name"/> is used.
    public string Nickname { get; set; } = string.Empty;

    /// What this PC is called everywhere here: its nickname, or its own name.
    [JsonIgnore]
    public string DisplayName => Nickname.Length > 0 ? Nickname : Name;

    /// Where to dial for <see cref="PcRoute.Address"/>: "name-or-address" or "name-or-address:port".
    public string Address { get; set; } = string.Empty;

    /// The PC's Tailscale address (given when pairing over the internet, or learned from the tailnet's peers and over the link); dialled for <see cref="PcRoute.Tailscale"/>. Empty = not known yet.
    public string TailscaleAddress { get; set; } = string.Empty;

    /// When the link to this PC last ended (it was reachable until then); shown on its row while it is offline.
    public DateTime? LastSeen { get; set; }

    /// Which display a per-display card of this PC drives, by the card's name ("Brightness"); a missing entry = all displays.
    public Dictionary<string, string> ScreenPicks { get; set; } = new();

    /// What it shared when it was last connected.
    public PcCatalog? Catalog { get; set; }

    /// How it said it can be woken (its network card, settings and network) the last time it was connected; null = never told (an older version).
    public WakeFacts? Wake { get; set; }

    /// How it went away, from the notice it sent just before: "sleep", "hibernate" or "shutdown"; null = no notice came (it was switched off or lost).
    public string? Away { get; set; }

    /// When the notice came (this PC's clock).
    public DateTime? AwayAt { get; set; }

    /// The device set-ups it last handed over (without scripts), so they can be imported while it is offline; null = never asked.
    public string? DeviceSetups { get; set; }
}

/// A "New PC" tab that is not finished: kept with how far it got, so it can be finished after a restart. It becomes the PC's own tab once paired.
public sealed class SetupTab
{
    public string Id { get; set; } = string.Empty;

    /// The chosen way: false = On this network, true = Over the internet.
    public bool Internet { get; set; }

    /// The address typed for a PC on another network (On this network).
    public string Address { get; set; } = string.Empty;

    /// The Tailscale name or address picked or typed (Over the internet).
    public string Target { get; set; } = string.Empty;
}

/// What Other PCs saves (other-pcs.json): this PC's own id, its paired PCs, the unfinished New PC tabs and their tab order.
public sealed class OtherPcsSettings
{
    public string OwnId { get; set; } = string.Empty;
    public List<PairedPc> Pairs { get; set; } = new();
    public List<SetupTab> SetupTabs { get; set; } = new();

    /// The ids of every tab (paired PCs and New PC tabs) in the order shown: the one saved order.
    public List<string> TabOrder { get; set; } = new();
}
