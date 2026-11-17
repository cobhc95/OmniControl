using OmniControl.App.Audio.Ir;
using OmniControl.App.Services;

namespace OmniControl.App.Audio.Engine;

/// <summary>What the folder watch compares the disk with: OmniControl's part of every config folder, and the IR files in use with the version each was loaded at.</summary>
internal sealed record ApoWatchInput(ApoTarget Target, IReadOnlyDictionary<string, long> IrFiles)
{
    public static readonly ApoWatchInput None = new(ApoTarget.None, new Dictionary<string, long>());
}

/// <summary>
/// One look at the disk: the target it was compared with, the folders whose OmniControl part is not as it should be and those where it is,
/// whether config.txt runs effects of its own where a chain is written, whether an IR file in use was replaced, and whether the PC slept
/// since the last look.
/// </summary>
internal sealed record ApoFindings(ApoTarget Target, IReadOnlyList<string> OutOfPlace, IReadOnlyList<string> InPlace, bool OwnEffects, bool IrReplaced, bool Resumed);

/// <summary>
/// Watches the Equalizer APO config folders and reads them back off the UI thread (<see cref="IApoConfigStore.Read"/>): after every change
/// notice (once the writer had <see cref="Timing.ApoChangeSettle"/>), whenever the owner asks (<see cref="Request"/>), and on a slow
/// watchdog (<see cref="Timing.ApoWatchdog"/>) for a missed notice or a folder that could not be watched. Decides nothing: every look is
/// handed to <c>found</c> on a worker thread. A watchdog look much later than due means the PC slept (<see cref="ApoFindings.Resumed"/>).
/// Every callback is guarded; <see cref="Dispose"/> stops the watchers and the timers.
/// </summary>
internal sealed class ApoWatch : IDisposable
{
    private readonly IApoConfigStore _store;
    private readonly Func<ApoWatchInput> _input;
    private readonly Action<ApoFindings> _found;
    private readonly ThrottledRun _check;
    private readonly Timer _watchdog;
    /// <summary>The folder watchers by folder; changed on the UI thread only (<see cref="Follow"/>).</summary>
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private DateTime _lastLook = DateTime.MinValue;
    private bool _resumed;
    private bool _disposed;

    /// <param name="input">What should be on disk now (read on a worker: it must be an immutable snapshot).</param>
    /// <param name="found">Each look's findings, on a worker thread.</param>
    public ApoWatch(IApoConfigStore store, Func<ApoWatchInput> input, Action<ApoFindings> found)
    {
        _store = store;
        _input = input;
        _found = found;
        _check = new ThrottledRun(Timing.ApoCheckGap, Check);
        _watchdog = new Timer(_ => Guard.Run("Effects: Equalizer APO watchdog", OnWatchdog), null, Timing.ApoWatchdog, Timing.ApoWatchdog);
    }

    /// <summary>Asks for a look at the disk soon (a change was made, the devices changed).</summary>
    public void Request() => _check.Request(Timing.ApoChangeSettle);

    /// <summary>Watches exactly <paramref name="folders"/> (UI thread). A folder that can't be watched is logged and left to the watchdog.</summary>
    public void Follow(IEnumerable<string> folders)
    {
        if (_disposed) return;
        var wanted = folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _watchers.Keys.Where(f => !wanted.Contains(f)).ToList())
        {
            _watchers[gone].Dispose();
            _watchers.Remove(gone);
        }
        foreach (var folder in wanted.Where(f => !_watchers.ContainsKey(f)))
            if (Watcher(folder) is { } watcher) _watchers[folder] = watcher;
    }

    private FileSystemWatcher? Watcher(string folder)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Guard.Log($"Effects: watching Equalizer APO's config folder {folder} (the watchdog still looks every {Timing.ApoWatchdog.TotalSeconds:0} s)", ex);
            return null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Request();

    /// <summary>The watcher's buffer overflowed or the folder went away: a look settles what is there now.</summary>
    private void OnError(object sender, ErrorEventArgs e) => Request();

    private void OnWatchdog()
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_lastLook != DateTime.MinValue && now - _lastLook > Timing.ApoResumeGap) _resumed = true;
            _lastLook = now;
        }
        _check.Request();
    }

    /// <summary>Worker: reads every folder of the target and every IR file in use, and hands over what was found.</summary>
    private void Check()
    {
        var input = _input();
        var outOfPlace = new List<string>();
        var inPlace = new List<string>();
        var own = false;
        var busy = false;
        foreach (var folder in input.Target.Folders)
        {
            if (_store.Read(folder, input.Target.WantedIn(folder)) is not { } facts)
            {
                busy = true;
                continue;
            }
            (facts.InPlace ? inPlace : outOfPlace).Add(folder);
            own |= facts.OwnEffects && input.Target.Files.ContainsKey(folder); // only where OmniControl's chain runs too
        }
        var replaced = input.IrFiles.Any(file => IrFileStamp.Of(file.Key) != file.Value);
        bool resumed;
        lock (_gate)
        {
            resumed = _resumed;
            _resumed = false;
        }
        if (busy) Request(); // a file was held this moment: look again shortly
        _found(new ApoFindings(input.Target, outOfPlace, inPlace, own, replaced, resumed));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Dispose();
        _check.Dispose();
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
