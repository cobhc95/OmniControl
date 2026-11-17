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

    /// <summary>Where to dial for <see cref="PcRoute.Address"/>: "name-or-address" or "name-or-address:port".</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The PC's Tailscale address (given when pairing over the internet, or learned from the tailnet's peers and over the link); dialled for <see cref="PcRoute.Tailscale"/>. Empty = not known yet.</summary>
    public string TailscaleAddress { get; set; } = string.Empty;

    /// <summary>When the link to this PC last ended (it was reachable until then); shown on its row while it is offline.</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>What it shared when it was last connected.</summary>
    public PcCatalog? Catalog { get; set; }

    /// <summary>The device set-ups it last handed over (without scripts), so they can be imported while it is offline; null = never asked.</summary>
    public string? DeviceSetups { get; set; }
}

/// <summary>What Other PCs saves (other-pcs.json): this PC's own id and its paired PCs.</summary>
public sealed class OtherPcsSettings
{
    public string OwnId { get; set; } = string.Empty;
    public List<PairedPc> Pairs { get; set; } = new();
}
