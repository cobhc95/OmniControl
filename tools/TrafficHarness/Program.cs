using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using System.Security.Principal;
using OmniControl.Core;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Resources;

namespace TrafficHarness;

/// Measures this program's own speed unlimited, then with a download and an upload limit set on itself through OmniControl's own
/// privileged path (NetworkWork: Windows' throttle rules for upload, Windows Packet Filter for download), and prints whether each limited
/// case lands within the tolerance. The parallel case runs two connections here and one in each of two child processes of the same program,
/// so it proves one shared limit per program. Every rule it set is taken out again, also on Ctrl+C.
internal static class Program
{
    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=20000000", UpUrl = "https://speed.cloudflare.com/__up";
    private const long DownTotal = 20_000_000, UpTotal = 10_000_000, DownLimit = 256 * 1024, UpLimit = 128 * 1024;
    private const double Tolerance = 0.15;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20), UnlimitedCap = TimeSpan.FromSeconds(60), Settle = TimeSpan.FromSeconds(2);

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--child", var seconds])
        {
            using var child = Client(HttpVersion.Version20);
            var (bytes, took) = await Download(child, long.MaxValue, TimeSpan.FromSeconds(double.Parse(seconds, CultureInfo.InvariantCulture)), CancellationToken.None);
            Console.WriteLine($"{bytes} {took.ToString(CultureInfo.InvariantCulture)}");
            return 0;
        }
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return Refuse("Run this from an administrator prompt: upload limits are Windows throttle rules and download limits open Windows Packet Filter, both need administrator rights. OmniControl's network service is not used (it answers only OmniControl).");
        if (Process.GetProcessesByName("OmniControl").Length > 0)
            return Refuse("Close OmniControl first and wait for its network service to stop (it stops itself about a minute later): both would change the same rules and the same driver.");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var path = Environment.ProcessPath!;
        var shaperFailure = (string?)null;
        var work = new NetworkWork(CoreTiming.Shaping, reason => shaperFailure = reason);
        var rows = new List<string[]>();
        var downloads = DownloadShaper.Installed;
        var h3 = QuicConnection.IsSupported;
        using var tcp = Client(HttpVersion.Version20);
        using var quic = Client(HttpVersion.Version30);
        try
        {
            Console.WriteLine("Measuring unlimited...");
            var free = new Dictionary<string, double>
            {
                ["Download TCP"] = Rate(await Download(tcp, DownTotal, UnlimitedCap, stop.Token)),
                ["Download TCP, 4 connections in 3 processes"] = await Parallel(path, stop.Token),
                ["Upload TCP"] = Rate(await Upload(tcp, UpTotal, UnlimitedCap, stop.Token)),
            };
            if (h3)
            {
                free["Download HTTP/3"] = Rate(await Download(quic, DownTotal, UnlimitedCap, stop.Token));
                free["Upload HTTP/3"] = Rate(await Upload(quic, UpTotal, UnlimitedCap, stop.Token));
            }

            Console.WriteLine("Setting the limits on this program...");
            var rule = new NetworkRule(ProgramIdentity.Normalize(path), path, (ulong)UpLimit * 8, false, downloads ? DownLimit : null);
            foreach (var problem in work.Sync(new[] { rule })) Console.WriteLine($"  Windows: {problem}");
            await Task.Delay(Settle, stop.Token); // the shaper reads the port tables and opens the driver

            foreach (var (name, unlimited) in free)
            {
                var upload = name.StartsWith("Upload", StringComparison.Ordinal);
                var target = upload ? UpLimit : DownLimit;
                if (!upload && !downloads)
                {
                    rows.Add(new[] { name, Kb(unlimited), Kb(target), "skipped", "Windows Packet Filter not installed" });
                    continue;
                }
                Console.WriteLine($"Measuring limited: {name}...");
                var client = name.Contains("HTTP/3", StringComparison.Ordinal) ? quic : tcp;
                var measured = name.Contains("4 connections", StringComparison.Ordinal) ? await Parallel(path, stop.Token)
                    : Rate(upload ? await Upload(client, long.MaxValue, Window, stop.Token) : await Download(client, long.MaxValue, Window, stop.Token));
                rows.Add(new[] { name, Kb(unlimited), Kb(target), Kb(measured), Math.Abs(measured - target) <= target * Tolerance ? "pass" : "FAIL" });
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped.");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"A transfer failed: {ex.Message}");
        }
        finally
        {
            work.StopDownloads();
            foreach (var problem in work.RemoveAll()) Console.WriteLine($"  Not taken out: {problem}");
            Console.WriteLine("Every rule this run set is taken out.");
        }

        if (!h3) Console.WriteLine("HTTP/3 (QUIC) is not supported here (msquic missing): its cases were skipped.");
        if (shaperFailure is not null) Console.WriteLine($"Download shaping stopped: {shaperFailure}");
        Print(rows);
        return rows.Any(r => r[4] == "FAIL") ? 1 : 0;
    }

    private static int Refuse(string why)
    {
        Console.WriteLine(why);
        return 2;
    }

    private static HttpClient Client(Version version) => new(new SocketsHttpHandler())
    {
        DefaultRequestVersion = version,
        DefaultVersionPolicy = version == HttpVersion.Version30 ? HttpVersionPolicy.RequestVersionExact : HttpVersionPolicy.RequestVersionOrLower,
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// Bytes received until <paramref name="most"/> or <paramref name="window"/> (new requests while time is left), and the seconds taken.
    private static async Task<(long Bytes, double Seconds)> Download(HttpClient client, long most, TimeSpan window, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        using var timeUp = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeUp.CancelAfter(window);
        var buffer = new byte[64 * 1024];
        long bytes = 0;
        try
        {
            while (bytes < most)
            {
                using var response = await client.GetAsync(DownUrl, HttpCompletionOption.ResponseHeadersRead, timeUp.Token);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(timeUp.Token);
                int read;
                while (bytes < most && (read = await body.ReadAsync(buffer, timeUp.Token)) > 0) bytes += read;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { } // the window ended: what arrived counts
        return (bytes, clock.Elapsed.TotalSeconds);
    }

    /// Bytes sent until <paramref name="most"/> or <paramref name="window"/>, and the seconds taken.
    private static async Task<(long Bytes, double Seconds)> Upload(HttpClient client, long most, TimeSpan window, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        var content = new Paced(most, clock, window);
        using var response = await client.PostAsync(UpUrl, content, stop);
        return (content.Sent, clock.Elapsed.TotalSeconds);
    }

    /// Two TCP connections here plus one in each of two child processes (the same program), for one window: their total rate.
    private static async Task<double> Parallel(string path, CancellationToken stop)
    {
        var children = Enumerable.Range(0, 2).Select(_ => Process.Start(new ProcessStartInfo(path, $"--child {Window.TotalSeconds.ToString(CultureInfo.InvariantCulture)}")
            { RedirectStandardOutput = true, UseShellExecute = false })!).ToList();
        try
        {
            // HTTP/1.1 clients, one each: HTTP/2 would carry both requests on one connection.
            using var first = Client(HttpVersion.Version11);
            using var second = Client(HttpVersion.Version11);
            var mine = await Task.WhenAll(Download(first, long.MaxValue, Window, stop), Download(second, long.MaxValue, Window, stop));
            long total = mine.Sum(m => m.Bytes);
            foreach (var child in children)
            {
                var line = (await child.StandardOutput.ReadToEndAsync(stop)).Trim().Split(' ');
                await child.WaitForExitAsync(stop);
                if (line.Length == 2) total += long.Parse(line[0], CultureInfo.InvariantCulture);
            }
            return total / Window.TotalSeconds;
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) child.Kill();
                child.Dispose();
            }
        }
    }

    private static double Rate((long Bytes, double Seconds) m) => m.Seconds > 0 ? m.Bytes / m.Seconds : 0;

    private static string Kb(double bytesPerSecond) => $"{bytesPerSecond / 1024:0} KB/s";

    private static void Print(List<string[]> rows)
    {
        var head = new[] { "Case", "Unlimited", "Target", "Measured", "Result" };
        var all = rows.Prepend(head).ToList();
        var widths = Enumerable.Range(0, head.Length).Select(i => all.Max(r => r[i].Length)).ToArray();
        Console.WriteLine();
        foreach (var row in all) Console.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))));
        Console.WriteLine($"Pass = within ±{Tolerance:P0} of the target.");
    }

    /// An upload body that writes until its size or its window is reached, counting what was written.
    private sealed class Paced(long most, Stopwatch clock, TimeSpan window) : HttpContent
    {
        public long Sent;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[16 * 1024];
            while (Sent < most && clock.Elapsed < window)
            {
                var size = (int)Math.Min(chunk.Length, most - Sent);
                await stream.WriteAsync(chunk.AsMemory(0, size));
                Sent += size;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
