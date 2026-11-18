using System.Text.Json.Serialization;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>How this PC reaches a paired PC: found on the local network (<see cref="Lan"/>), at an address typed for it (<see cref="Address"/>: ZeroTier or any other network that carries it), or at the Tailscale address both PCs gave each other when they paired (<see cref="Tailscale"/>).</summary>
public enum PcRoute { Lan, Address, Tailscale }

/// <summary>What a paired PC last shared (its basic set), kept so its entries stay listed, unavailable, while it is offline and after a restart.</summary>
public sealed class PcCatalog
{
    public List<SharedState> States { get; set; } = new();
    public List<SharedAction> Actions { get; set; } = new();
}

/// <summary>One paired PC as it is saved. Its pairing key is not here: Windows' credential store keeps it.</summary>
public sealed class PairedPc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PcRoute Route { get; set; } = PcRoute.Lan;

    /// <summary>The name the user gave this PC on this PC only (never sent to it); empty = none, <see cref="Name"/> is used.</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>What this PC is called everywhere here: its nickname, or its own name.</summary>
    [JsonIgnore]
    public string DisplayName => Nickname.Length > 0 ? Nickname : Name;

    /// <summary>Where to dial for <see cref="PcRoute.Address"/>: "name-or-address" or "name-or-address:port".</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The PC's Tailscale address (given when pairing over the internet, or learned from the tailnet's peers and over the link); dialled for <see cref="PcRoute.Tailscale"/>. Empty = not known yet.</summary>
    public string TailscaleAddress { get; set; } = string.Empty;

    /// <summary>When the link to this PC last ended (it was reachable until then); shown on its row while it is offline.</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>Which display a per-display card of this PC drives, by the card's name ("Brightness"); a missing entry = all displays.</summary>
    public Dictionary<string, string> ScreenPicks { get; set; } = new();

    /// <summary>What it shared when it was last connected.</summary>
    public PcCatalog? Catalog { get; set; }

    /// <summary>How it said it can be woken (its network card, settings and network) the last time it was connected; null = never told (an older version).</summary>
    public WakeFacts? Wake { get; set; }

    /// <summary>How it went away, from the notice it sent just before: "sleep", "hibernate" or "shutdown"; null = no notice came (it was switched off or lost).</summary>
    public string? Away { get; set; }

    /// <summary>When the notice came (this PC's clock).</summary>
    public DateTime? AwayAt { get; set; }

    /// <summary>The device set-ups it last handed over (without scripts), so they can be imported while it is offline; null = never asked.</summary>
    public string? DeviceSetups { get; set; }
}

/// <summary>A "New PC" tab that is not finished (docs/mockups/pair-pc-in-tab.html): kept with how far it got, so it can be finished after a restart. It becomes the PC's own tab once paired.</summary>
public sealed class SetupTab
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The chosen way: false = On this network, true = Over the internet.</summary>
    public bool Internet { get; set; }

    /// <summary>The address typed for a PC on another network (On this network).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The Tailscale name or address picked or typed (Over the internet).</summary>
    public string Target { get; set; } = string.Empty;
}

/// <summary>What Other PCs saves (other-pcs.json): this PC's own id, its paired PCs, the unfinished New PC tabs and their tab order.</summary>
public sealed class OtherPcsSettings
{
    public string OwnId { get; set; } = string.Empty;
    public List<PairedPc> Pairs { get; set; } = new();
    public List<SetupTab> SetupTabs { get; set; } = new();

    /// <summary>The ids of every tab (paired PCs and New PC tabs) in the order shown: the one saved order.</summary>
    public List<string> TabOrder { get; set; } = new();
}
