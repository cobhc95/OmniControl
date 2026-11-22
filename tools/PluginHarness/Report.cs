using System.Globalization;
using System.Text;

namespace PluginHarness;

/// The console table and the CSV.
internal static class Report
{
    private static string D(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    private static string Yn(bool v) => v ? "yes" : "no";

    private static string Verdict(Result r)
    {
        if (!r.Loaded) return "NOT LOADED";
        if (r.Silent) return "silent output";
        if (r.NonFinite > 0) return "NaN/Inf output";
        if (r.RestoreResult == "DIFFERENT") return "state restore differs";
        if (!r.ChangeAudible) return r.IdenticalToInput ? "passes through, no parameter changes it" : "no parameter change audible";
        return "ok";
    }

    public static void Print(IReadOnlyList<Result> results)
    {
        Console.WriteLine();
        string[] head = ["Plug-in", "Fmt", "Load", "In/Out", "Params", "Chunks", "Lat", "Silent", "Same as in", "Null dB", "Default max band", "Change (max band)", "Restore", "Verdict"];
        var rows = results.Select(r => new[]
        {
            Path.GetFileName(r.File.TrimEnd('\\')), r.Format, r.Loaded ? "yes" : "NO", r.Loaded ? $"{r.Inputs}/{r.Outputs}" : "-",
            r.Loaded ? r.Params.ToString(CultureInfo.InvariantCulture) : "-", r.Loaded ? r.Chunks : "-", r.Loaded ? r.LatencyFrames.ToString(CultureInfo.InvariantCulture) : "-",
            r.Loaded ? Yn(r.Silent) : "-", r.Loaded ? Yn(r.IdenticalToInput) : "-", r.Loaded ? D(r.NullDb) : "-", r.Loaded ? D(r.MaxBandDb) + " dB" : "-",
            r.Loaded ? $"{Yn(r.ChangeAudible)} ({D(r.ChangeMaxBandDb)} dB)" : "-", r.Loaded ? r.RestoreResult : "-", Verdict(r),
        }).ToList();
        var widths = head.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(row => row[i].Length))).ToArray();
        string Line(string[] cells) => string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i])));
        Console.WriteLine(Line(head));
        Console.WriteLine(new string('-', widths.Sum() + 2 * (widths.Length - 1)));
        foreach (var row in rows) Console.WriteLine(Line(row));
        foreach (var r in results.Where(r => r.Error.Length > 0)) Console.WriteLine($"! {Path.GetFileName(r.File.TrimEnd('\\'))}: {r.Error}{(r.Exit is "" or "0" ? "" : $" (exit {r.Exit})")}");
    }

    public static string Csv(IReadOnlyList<Result> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("File,Format,Name,Loaded,Error,Inputs,Outputs,Params,Chunks,LatencyFrames,StateBytes,Silent,OutRmsDb,IdenticalToInput,NullDb,DefaultMaxBandDb,NonFinite,ChangeMode,ChangeAudible,ChangeMaxBandDb,Restore,RestoreMaxBandDb,Exit,Verdict");
        foreach (var r in results)
            sb.AppendLine(string.Join(",", new[]
            {
                r.File, r.Format, r.Name, Yn(r.Loaded), r.Error, r.Inputs.ToString(CultureInfo.InvariantCulture), r.Outputs.ToString(CultureInfo.InvariantCulture),
                r.Params.ToString(CultureInfo.InvariantCulture), r.Chunks, r.LatencyFrames.ToString(CultureInfo.InvariantCulture), r.StateBytes.ToString(CultureInfo.InvariantCulture),
                Yn(r.Silent), D(r.OutRmsDb), Yn(r.IdenticalToInput), D(r.NullDb), D(r.MaxBandDb), r.NonFinite.ToString(CultureInfo.InvariantCulture), r.ChangeMode,
                Yn(r.ChangeAudible), D(r.ChangeMaxBandDb), r.RestoreResult, D(r.RestoreMaxBandDb), r.Exit, Verdict(r),
            }.Select(Quote)));
        return sb.ToString();
    }

    private static string Quote(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
}
