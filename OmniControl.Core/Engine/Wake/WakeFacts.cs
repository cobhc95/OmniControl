namespace OmniControl.Core.Engine.Wake;

/// <summary>
/// What one PC knows about being woken over the network, read from its active network card (<see cref="WakeReader"/>): which card, its address
/// and network, and each setting a wake needs. A plain class so it travels between paired PCs as it is. <see cref="Known"/> is false when the
/// settings could not be read (no card found, or Windows refused the question): every other field is then empty, never a guess.
/// </summary>
public sealed class WakeFacts
{
    public bool Known { get; set; }

    /// <summary>The card's name as Windows shows it ("Intel AX210").</summary>
    public string Adapter { get; set; } = string.Empty;

    /// <summary>The card's address, "AA:BB:CC:DD:EE:FF" (what a magic packet carries).</summary>
    public string Mac { get; set; } = string.Empty;

    /// <summary>The card's IPv4 network, "192.168.1.0/24": only a PC on the same network can send it a magic packet.</summary>
    public string Subnet { get; set; } = string.Empty;

    /// <summary>The card and its driver can wake the PC at all.</summary>
    public bool CardCanWake { get; set; }

    /// <summary>The card's own "Wake on Magic Packet" setting.</summary>
    public bool MagicPacketOn { get; set; }

    /// <summary>Windows lets the card wake the PC ("Allow this device to wake the computer").</summary>
    public bool Armed { get; set; }

    /// <summary>Only a magic packet may wake it (other traffic may not).</summary>
    public bool MagicOnly { get; set; }

    /// <summary>Fast Startup is on: a shut down is then a hibernation the card cannot wake from.</summary>
    public bool FastStartupOn { get; set; }

    /// <summary>The PC sleeps in Modern Standby (S0) rather than classic sleep (S3).</summary>
    public bool ModernStandby { get; set; }

    /// <summary>OmniControl on that PC has changed settings it can put back ("Undo my changes" is offered).</summary>
    public bool CanUndo { get; set; }

    /// <summary>The three settings a wake needs are all on.</summary>
    public bool Ready => Known && CardCanWake && MagicPacketOn && Armed;

    /// <summary>The facts as one text: equal facts give equal text (used to see whether they changed).</summary>
    public string Key => string.Join('|', Known, Adapter, Mac, Subnet, CardCanWake, MagicPacketOn, Armed, MagicOnly, FastStartupOn, ModernStandby, CanUndo);
}

/// <summary>A card setting <see cref="WakeFixer"/> can change (each is one row of the set-up window).</summary>
public enum WakeFix { MagicPacket, Arm, MagicOnly, FastStartup }
