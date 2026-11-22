using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PluginHarness;

/// Parent: finds the plug-ins and runs each in a child process of this program (so a native crash is only that row). Child
/// (`--one <file>`): measures one plug-in and prints one `RESULT:` JSON line.
internal static class Program
{
    private const string ChildFlag = "--one", Marker = "RESULT:";
    private static readonly TimeSpan ChildLimit = TimeSpan.FromSeconds(90);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == ChildFlag) return RunChild(args[1]);
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: PluginHarness <folder> [<folder> ...]   (VST2 *.dll and *.vst3 files and bundles, searched recursively)");
            return 2;
        }
        var files = args.SelectMany(Find).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        Console.WriteLine($"{files.Count} plug-in file(s) found.");
        var results = new List<Result>();
        foreach (var file in files)
        {
            Console.WriteLine($"  testing {file}");
            results.Add(RunInChild(file));
        }
        Report.Print(results);
        var csv = Path.Combine(AppContext.BaseDirectory, "harness-results.csv");
        File.WriteAllText(csv, Report.Csv(results), new UTF8Encoding(true));
        Console.WriteLine($"Written {csv}");
        return 0;
    }

    private static IEnumerable<string> Find(string folder)
    {
        if (!Directory.Exists(folder)) { Console.WriteLine($"Not a folder: {folder}"); yield break; }
        foreach (var bundle in Directory.EnumerateDirectories(folder, "*.vst3", SearchOption.AllDirectories)) yield return bundle;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) && !InsideBundle(file, folder)) yield return file;
            else if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !InsideBundle(file, folder) && OmniControl.Core.Engine.Vst2File.Problem(file) is not "This DLL isn't a VST2 plug-in.")
                yield return file;
        }
    }

    // A bundle's own binary (Contents\x86_64-win\X.vst3) is tested through the bundle folder.
    private static bool InsideBundle(string file, string root) =>
        Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).SkipLast(1).Any(part => part.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase));

    private static int RunChild(string file)
    {
        var result = Probe.Run(file);
        Console.Out.WriteLine(Marker + JsonSerializer.Serialize(result));
        Console.Out.Flush();
        return 0;
    }

    private static Result RunInChild(string file)
    {
        var format = file.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "VST3" : "VST2";
        var failed = new Result { File = file, Format = format };
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(ChildFlag);
        start.ArgumentList.Add(file);
        try
        {
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(ChildLimit))
            {
                child.Kill(true);
                failed.Error = "Hung for more than 90 s (killed).";
                failed.Exit = "timeout";
                return failed;
            }
            child.WaitForExit();
            var line = stdout.Result.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
            if (child.ExitCode == 0 && line is not null && JsonSerializer.Deserialize<Result>(line[Marker.Length..]) is { } result)
            {
                result.Exit = "0";
                return result;
            }
            failed.Error = child.ExitCode == 0 ? "No result from the test process." : "Native crash while testing.";
            failed.Exit = "0x" + child.ExitCode.ToString("X8", CultureInfo.InvariantCulture);
            return failed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            failed.Error = "Test process failed: " + ex.Message;
            return failed;
        }
    }
}
