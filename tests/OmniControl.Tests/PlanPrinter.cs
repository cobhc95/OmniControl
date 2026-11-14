using OmniControl.App.Audio.Engine;

namespace OmniControl.Tests;

/// <summary>A plan as text, names instead of endpoint ids, for the test output (the evidence the owner's report quotes).</summary>
internal static class PlanPrinter
{
    private static string N(string? id) => id switch
    {
        null => "(Windows' default)",
        OwnerPc.CableInput => "CABLE Input",
        OwnerPc.Cable16 => "CABLE In 16ch",
        OwnerPc.Speaker => "Speaker",
        OwnerPc.CableA => "CABLE-A Input",
        OwnerPc.Helium => "Helium",
        OwnerPc.Spotify => "Spotify",
        OwnerPc.Claude => "Claude",
        _ => id,
    };

    public static string Print(RoutePlan plan)
    {
        var lines = new List<string>();
        foreach (var r in plan.Runs.Values)
            lines.Add($"run   {N(r.Key)}: {r.Kind} cable={(r.Cable is null ? "none" : N(r.Cable))} outputs=[{string.Join(", ", r.Outputs.Select(N))}]");
        foreach (var (k, s) in plan.Blocked) lines.Add($"block {N(k)}: {s.State} \"{s.Text}\"");
        lines.Add($"bus routes: [{string.Join(", ", plan.BusRoutes.Select(N))}]");
        lines.Add($"cables in use: [{string.Join(", ", plan.CablesInUse.Select(N))}]");
        return string.Join(Environment.NewLine, lines);
    }
}
