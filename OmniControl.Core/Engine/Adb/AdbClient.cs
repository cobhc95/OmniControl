using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine;

/// What an adb command printed and whether it worked.
public sealed record AdbResult(bool Ok, string Text);

/// The screen and input of one Android device or emulator, as a simulated device needs them (a fake of it runs the steps in tests).
public interface IAdbScreen
{
    /// The screen as uiautomator XML (<see cref="AdbResult.Text"/>).
    Task<AdbResult> DumpAsync();
    Task<AdbResult> TapAsync(int x, int y);
    Task<AdbResult> SwipeAsync(int fromX, int fromY, int toX, int toY);
    Task<AdbResult> TypeAsync(string text);
    Task<AdbResult> BackAsync();
    Task<AdbResult> OpenAppAsync(string package);

    /// The screen's size in pixels (<see cref="AdbResult.Text"/> is <c>1080x1920</c>).
    Task<AdbResult> SizeAsync();

    /// A screenshot as PNG bytes (<c>screencap -p</c>); null when adb could not take one.
    Task<byte[]?> ScreenshotAsync();
}

/// adb for one device: every call starts <c>adb -s &lt;serial&gt; ...</c> hidden and stopped after <see cref="CoreTiming.AdbCommandTimeoutMs"/>.
/// Anything put into the device's shell is a number or a package name that passed <see cref="SimSteps.IsPackage"/>, or typed text that is quoted.
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

    /// Joins the emulator at <c>127.0.0.1:5555</c> to adb. A serial without a colon (<c>emulator-5554</c>) needs no connecting.
    public static async Task<AdbResult> ConnectAsync(string adbPath, string address)
    {
        if (!address.Contains(':')) return new AdbResult(true, "no connect needed");
        var result = await RunAsync(adbPath, new[] { "connect", address });
        // adb prints "connected to ..." or "already connected to ..." and still exits with 0 when it could not ("cannot connect to ...").
        return result with { Ok = result.Ok && result.Text.Contains("connected to", StringComparison.OrdinalIgnoreCase) && !result.Text.Contains("cannot", StringComparison.OrdinalIgnoreCase) };
    }

    /// The serials adb lists as ready (<c>adb devices</c>); empty when adb could not be asked.
    public static async Task<IReadOnlySet<string>> OnlineSerialsAsync(string adbPath)
    {
        var result = await RunAsync(adbPath, new[] { "devices" });
        if (!result.Ok) return new HashSet<string>();
        return result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t', StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[1] == "device").Select(p => p[0]).ToHashSet();
    }

    /// Waits until the device is up and Android says it finished starting (<c>sys.boot_completed</c>), looking again every <paramref name="poll"/>;
    /// false when <paramref name="timeout"/> passed or <paramref name="cancel"/> was raised first.
    public static async Task<bool> WaitForBootAsync(string adbPath, string serial, TimeSpan timeout, TimeSpan poll, CancellationToken cancel)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until && !cancel.IsCancellationRequested)
        {
            var booted = await RunAsync(adbPath, new[] { "-s", serial, "shell", "getprop", "sys.boot_completed" });
            if (booted.Ok && booted.Text.Trim() == "1") return true;
            try { await Task.Delay(poll, cancel); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    /// Shuts the emulator down cleanly so Android saves its state: <c>adb emu kill</c> (Android Studio's), else Android's own power off.
    public static async Task<AdbResult> ShutDownAsync(string adbPath, string serial)
    {
        var killed = await RunAsync(adbPath, new[] { "-s", serial, "emu", "kill" });
        return killed.Ok ? killed : await RunAsync(adbPath, new[] { "-s", serial, "shell", "reboot", "-p" });
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

    /// Wakes the screen and lifts the lock screen; a hidden emulator comes up asleep and a sleeping screen answers with a black picture.
    public async Task<AdbResult> WakeScreenAsync()
    {
        var woken = await ShellAsync("input keyevent KEYCODE_WAKEUP");
        return woken.Ok ? await ShellAsync("wm dismiss-keyguard") : woken;
    }

    public Task<AdbResult> BackAsync() => ShellAsync("input keyevent KEYCODE_BACK");

    public Task<AdbResult> OpenAppAsync(string package) =>
        SimSteps.IsPackage(package) ? ShellAsync($"monkey -p {package} -c android.intent.category.LAUNCHER 1") : Task.FromResult(new AdbResult(false, "not a package name"));

    public Task<AdbResult> SizeAsync() => ShellAsync("wm size");

    public async Task<byte[]?> ScreenshotAsync()
    {
        var (ok, output, _) = await RunRawAsync(_adb, new[] { "-s", _serial, "exec-out", "screencap", "-p" });
        return ok && output.Length > 0 ? output : null;
    }

    /// The app that has the screen now (<c>dumpsys window</c>); empty when it cannot be told.
    public async Task<string> CurrentPackageAsync()
    {
        var result = await ShellAsync("dumpsys window");
        return result.Ok ? CurrentPackageOf(result.Text) : string.Empty;
    }

    /// The name the app shows under its icon, as Android's package dump states it (<c>application-label</c> or <c>nonLocalizedLabel</c>);
    /// else the maker's part of the package made readable (<see cref="LabelFallback"/>). The recorder prefers the words under the icon tapped.
    public async Task<string> AppLabelAsync(string package)
    {
        if (!SimSteps.IsPackage(package)) return package;
        var result = await ShellAsync($"dumpsys package {package}");
        return result.Ok ? AppLabelOf(result.Text, package) : LabelFallback(package);
    }

    public static string AppLabelOf(string dumpsys, string package)
    {
        var m = Regex.Match(dumpsys, @"application-label(?:-[\w-]+)?:'([^']+)'|nonLocalizedLabel=([^\r\n]+)");
        var label = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim() : string.Empty;
        return label.Length > 0 && label != "null" ? label : LabelFallback(package);
    }

    /// A readable name when the app's own is not known: the package's second part ("com.tplink.iot" = "Tplink"), not its last word, which is often generic.
    public static string LabelFallback(string package)
    {
        var parts = package.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return SimSteps.Humanise(parts.Length >= 2 ? parts[1] : package);
    }

    /// The package in <c>mCurrentFocus=Window{... u0 com.example/com.example.Main}</c>; empty when there is none.
    public static string CurrentPackageOf(string dumpsys) => Focus.Match(dumpsys) is { Success: true } m ? m.Groups[1].Value : string.Empty;

    /// The width and height in <c>Physical size: 1080x1920</c>; null when it is not there.
    public static (int Width, int Height)? SizeOf(string wmSize)
    {
        var m = Size.Match(wmSize);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    /// Text for <c>input text</c>: spaces become %s and anything the device's shell would read is escaped, inside single quotes.
    public static string EscapeForInput(string text) => text.Replace("'", "'\\''").Replace(" ", "%s");

    private Task<AdbResult> ShellAsync(string command) => RunAsync(_adb, new[] { "-s", _serial, "shell", command });

    internal static async Task<AdbResult> RunAsync(string adb, IReadOnlyList<string> arguments)
    {
        var (ok, output, problem) = await RunRawAsync(adb, arguments);
        var text = Encoding.UTF8.GetString(output);
        return new AdbResult(ok, ok || problem.Length == 0 ? text : problem);
    }

    /// Runs adb and gives what it printed as bytes (a screenshot is binary), its error text, and whether it exited with 0.
    private static async Task<(bool Ok, byte[] Output, string Problem)> RunRawAsync(string adb, IReadOnlyList<string> arguments)
    {
        NormalRights.Started started;
        try
        {
            started = NormalRights.Start(adb, arguments, hidden: true, readOutput: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (false, Array.Empty<byte>(), $"couldn't start adb: {ex.Message}");
        }
        using (started)
        {
            var process = started.Process;
            using var buffer = new MemoryStream();
            var output = started.Output!.BaseStream.CopyToAsync(buffer);
            var errors = started.Errors!.ReadToEndAsync();
            using var limit = new CancellationTokenSource(CoreTiming.AdbCommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(limit.Token);
                await output;
                var problem = (await errors).Trim();
                return (process.ExitCode == 0, buffer.ToArray(), problem);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* it ended just now */ }
                return (false, Array.Empty<byte>(), $"adb didn't answer within {CoreTiming.AdbCommandTimeoutMs / 1000} s");
            }
        }
    }
}
