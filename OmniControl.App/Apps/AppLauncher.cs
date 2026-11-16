using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Apps;

/// <summary>
/// Knows which of the catalogue's apps are installed (<see cref="AppLookup"/>, off the UI thread) and starts them, as the normal user and
/// rate limited (<see cref="LaunchLimiter"/>). The program an input (a button, gesture or rule) chose for itself beats the one found.
/// </summary>
public sealed class AppLauncher
{
    private readonly LaunchLimiter _limiter = new();
    private readonly Action<string> _report;
    private IReadOnlyDictionary<string, AppTarget> _found = new Dictionary<string, AppTarget>();
    private DateTime _scanned = DateTime.MinValue;
    private bool _scanning;

    public AppLauncher(Action<string> report) => _report = report;

    /// <summary>Raised on the UI thread after a look for installed apps finished.</summary>
    public event Action? Scanned;

    /// <summary>True when the app was found on this PC.</summary>
    public bool IsInstalled(string id) => _found.ContainsKey(id);

    /// <summary>The program file an installed app starts (what a running copy is called), or null for an app that is not installed or is not a file (a Store app, a protocol).</summary>
    public string? ProgramOf(string id)
    {
        if (!_found.TryGetValue(id, out var target) || !target.IsFile) return null;
        // Discord and the old Teams start through their updater, which names the real program in its arguments.
        const string StartsThrough = "--processStart ";
        return target.Arguments.StartsWith(StartsThrough, StringComparison.Ordinal) ? target.Arguments[StartsThrough.Length..] : target.Path;
    }

    /// <summary>Looks for one app now (cheap for an app with a known program), so an app the user just added can be opened at once.</summary>
    public void Include(AppEntry app)
    {
        var one = AppLookup.Find(new Dictionary<string, AppLocator> { [app.Id] = app.Locator });
        _found = _found.Where(f => f.Key != app.Id).Concat(one).ToDictionary(f => f.Key, f => f.Value);
    }

    /// <summary>Looks for the installed apps again, unless that was done a moment ago (<see cref="Timing.AppScanGap"/>) or is going on.</summary>
    public void Scan(IEnumerable<AppEntry> apps, bool force = false)
    {
        if (_scanning || (!force && DateTime.UtcNow - _scanned < Timing.AppScanGap)) return;
        _scanning = true;
        var locators = apps.ToDictionary(a => a.Id, a => a.Locator);
        Task.Run(() => AppLookup.Find(locators)).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            _scanning = false;
            _scanned = DateTime.UtcNow;
            if (task.IsCompletedSuccessfully) _found = task.Result;
            Scanned?.Invoke();
        }));
    }

    /// <summary>Opens the app; <paramref name="own"/> holds what an input chose for itself (empty parts mean the app's own).</summary>
    public void Open(AppEntry app, LaunchSettings? own = null)
    {
        if (!_found.TryGetValue(app.Id, out var target))
        {
            _report($"{app.Title} isn't installed any more.");
            return;
        }
        var path = own is { Path.Length: > 0 } ? own.Path : target.Path;
        var chosenProgram = path != target.Path;
        var arguments = own is { Arguments.Length: > 0 } ? own.Arguments : chosenProgram ? string.Empty : target.Arguments;
        var startIn = own is { StartIn.Length: > 0 } ? own.StartIn : chosenProgram ? string.Empty : target.StartIn;
        if (!_limiter.TryAcquire())
        {
            _report("Stopped an app from being opened again and again. Check the button, gesture or rule that opens it.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = arguments, WorkingDirectory = startIn });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _report($"Couldn't open {app.Title}: {ex.Message}");
        }
    }
}
