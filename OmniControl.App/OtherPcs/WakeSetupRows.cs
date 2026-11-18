using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>How a row of the wake set-up looks: done (tick), needs fixing, optional (muted), the person must do it, or the test.</summary>
public enum WakeRowKind { Done, Needed, Optional, You, Test }

/// <summary>One row of the wake set-up (docs/mockups/set-up-wake.html): its words, a muted detail, a muted note under it and the fix it offers (none = nothing to press).</summary>
public sealed record WakeRowSpec(string Id, WakeRowKind Kind, string Label, string Detail, string Note, WakeFix? Fix);

/// <summary>THE rows of the wake set-up, made from what the PC reported. Pure.</summary>
public static class WakeSetupRows
{
    public const string TestId = "test";

    public static IReadOnlyList<WakeRowSpec> Of(WakeFacts? facts, string name, bool canTest = true)
    {
        var f = facts ?? new WakeFacts();
        var rows = new List<WakeRowSpec>
        {
            new WakeRowSpec("card", f.CardCanWake ? WakeRowKind.Done : WakeRowKind.Needed, "Network card can wake this PC", f.Adapter,
                f.CardCanWake ? string.Empty : f.Known ? "This card or its driver cannot wake the PC, so it cannot be woken over the network." : "Not read yet. Press Check again while the PC is on.", null),
            Row("magic", f.MagicPacketOn, "Wake on Magic Packet", "The card's own setting.", WakeFix.MagicPacket),
            Row("arm", f.Armed, "Allow the card to wake the PC", "Windows' own permission for the card.", WakeFix.Arm),
            Row("only", f.MagicOnly, "Only a magic packet can wake it", "So other network traffic does not wake it.", WakeFix.MagicOnly),
            new WakeRowSpec("fast", f.Known && !f.FastStartupOn ? WakeRowKind.Done : WakeRowKind.Optional, "Fast Startup off", "optional", "Only needed to wake it from shut down.", WakeFix.FastStartup),
            new WakeRowSpec("bios", WakeRowKind.You, "BIOS wake setting", "you do this · optional", "Only for waking from shut down. Look for \"Wake on LAN\" in the BIOS.", null),
        };
        if (canTest) rows.Add(new WakeRowSpec(TestId, WakeRowKind.Test, "Test wake", string.Empty, $"Puts {name} to sleep, then wakes it from here.", null));
        return rows;
    }

    private static WakeRowSpec Row(string id, bool on, string label, string note, WakeFix fix) =>
        new(id, on ? WakeRowKind.Done : WakeRowKind.Needed, label, on ? "On" : string.Empty, on ? string.Empty : note, fix);

    /// <summary>The fixes "Fix all" asks for: every required row that is not done (the optional Fast Startup row is always its own choice).</summary>
    public static IReadOnlyList<WakeFix> Required(IEnumerable<WakeRowSpec> rows) =>
        rows.Where(r => r is { Kind: WakeRowKind.Needed, Fix: not null }).Select(r => r.Fix!.Value).ToList();
}
