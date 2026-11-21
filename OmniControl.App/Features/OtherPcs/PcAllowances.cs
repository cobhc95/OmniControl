namespace OmniControl.App.Features.OtherPcs;

/// THE words of what a paired PC may do here: the title and the three switches, shown alike in its ⋯ menu and while pairing.
internal static class PcAllowances
{
    public static string Title(string name) => $"What {name} may do here";

    public static readonly IReadOnlyList<(PcAllowance What, string Label, string Tip)> All = new[]
    {
        (PcAllowance.Power, "Power actions", "Off: it can't lock, sign out, sleep, restart or shut down this PC, or wake another PC through it. On, all but lock wait for this PC's own countdown, which you can cancel."),
        (PcAllowance.Actions, "Other actions and settings", "Off: it can't run this PC's other shared actions or change its shared settings, such as the volume."),
        (PcAllowance.ReadSettings, "Read settings and devices", "Off: it can't copy this PC's settings or device set-ups. On, each read is said in the status line."),
    };
}
