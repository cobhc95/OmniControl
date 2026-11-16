using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine;

/// <summary>What an adb command printed and whether it worked.</summary>
public sealed record AdbResult(bool Ok, string Text);

/// <summary>The screen and input of one Android device or emulator, as a simulated device needs them (a fake of it runs the steps in tests).</summary>
public interface IAdbScreen
{
    /// <summary>The screen as uiautomator XML (<see cref="AdbResult.Text"/>).</summary>
    Task<AdbResult> DumpAsync();
    Task<AdbResult> TapAsync(int x, int y);
    Task<AdbResult> SwipeAsync(int fromX, int fromY, int toX, int toY);
    Task<AdbResult> TypeAsync(string text);
    Task<AdbResult> BackAsync();
    Task<AdbResult> OpenAppAsync(string package);

    /// <summary>The screen's size in pixels (<see cref="AdbResult.Text"/> is <c>1080x1920</c>).</summary>
    Task<AdbResult> SizeAsync();
}

/// <summary>
/// adb for one device: every call starts <c>adb -s &lt;serial&gt; ...</c> hidden and stopped after <see cref="CoreTiming.AdbCommandTimeoutMs"/>.
/// Anything put into the device's shell is a number or a package name that passed <see cref="SimSteps.IsPackage"/>, or typed text that is quoted.
/// </summary>
public sealed class AdbClient : IAdbScreen
{
    private const string DumpFile = "/sdcard/omnicontrol-ui.xml";
    private static readonly Regex Focus = new(@"mCurrentFocus=Window\{[^}]*?\s([A-Za-z0-9_.]+)/[A-Za-z0-9_.$]+", RegexOptions.Compiled);
    private static readonly Regex Size = new(@"(\d+)x(\d+)", RegexOptions.Compiled);

    private readonly string _adb;
    private readonly string _serial;

    public AdbClient(string adbPath, string serial)
    {
        _adb = adbPath;
        _serial = serial;
    }

    /// <summary>Joins the emulator at <c>127.0.0.1:5555</c> to adb. A serial without a colon (<c>emulator-5554</c>) needs no connecting.</summary>
    public static async Task<AdbResult> ConnectAsync(string adbPath, string address)
    {
        if (!address.Contains(':')) return new AdbResult(true, "no connect needed");
        var result = await RunAsync(adbPath, new[] { "connect", address });
        // adb prints "connected to ..." or "already connected to ..." and still exits with 0 when it could not ("cannot connect to ...").
        return result with { Ok = result.Ok && result.Text.Contains("connected to", StringComparison.OrdinalIgnoreCase) && !result.Text.Contains("cannot", StringComparison.OrdinalIgnoreCase) };
    }

    public Task<AdbResult> DumpAsync() => DumpToFileAsync();

    private async Task<AdbResult> DumpToFileAsync()
    {
        var made = await ShellAsync($"uiautomator dump {DumpFile}");
        return made.Ok ? await RunAsync(_adb, new[] { "-s", _serial, "exec-out", "cat", DumpFile }) : made;
    }

    public Task<AdbResult> TapAsync(int x, int y) => ShellAsync($"input tap {x} {y}");

    public Task<AdbResult> SwipeAsync(int fromX, int fromY, int toX, int toY) => ShellAsync($"input swipe {fromX} {fromY} {toX} {toY} {CoreTiming.AdbSwipeMs}");

    public Task<AdbResult> TypeAsync(string text) => ShellAsync($"input text '{EscapeForInput(text)}'");

    public Task<AdbResult> BackAsync() => ShellAsync("input keyevent KEYCODE_BACK");

    public Task<AdbResult> OpenAppAsync(string package) =>
        SimSteps.IsPackage(package) ? ShellAsync($"monkey -p {package} -c android.intent.category.LAUNCHER 1") : Task.FromResult(new AdbResult(false, "not a package name"));

    public Task<AdbResult> SizeAsync() => ShellAsync("wm size");

    /// <summary>The app that has the screen now (<c>dumpsys window</c>); empty when it cannot be told.</summary>
    public async Task<string> CurrentPackageAsync()
    {
        var result = await ShellAsync("dumpsys window");
        return result.Ok ? CurrentPackageOf(result.Text) : string.Empty;
    }

    /// <summary>The package in <c>mCurrentFocus=Window{... u0 com.example/com.example.Main}</c>; empty when there is none.</summary>
    public static string CurrentPackageOf(string dumpsys) => Focus.Match(dumpsys) is { Success: true } m ? m.Groups[1].Value : string.Empty;

    /// <summary>The width and height in <c>Physical size: 1080x1920</c>; null when it is not there.</summary>
    public static (int Width, int Height)? SizeOf(string wmSize)
    {
        var m = Size.Match(wmSize);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    /// <summary>Text for <c>input text</c>: spaces become %s and anything the device's shell would read is escaped, inside single quotes.</summary>
    public static string EscapeForInput(string text) => text.Replace("'", "'\''").Replace(" ", "%s");

    private Task<AdbResult> ShellAsync(string command) => RunAsync(_adb, new[] { "-s", _serial, "shell", command });

    internal static async Task<AdbResult> RunAsync(string adb, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(adb)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start adb.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new AdbResult(false, $"couldn't start adb: {ex.Message}");
        }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(CoreTiming.AdbCommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(limit.Token);
                var text = await output;
                var problem = await errors;
                return new AdbResult(process.ExitCode == 0, process.ExitCode == 0 || problem.Length == 0 ? text : problem.Trim());
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* it ended just now */ }
                return new AdbResult(false, $"adb didn't answer within {CoreTiming.AdbCommandTimeoutMs / 1000} s");
            }
        }
    }
}
