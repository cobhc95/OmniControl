using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OmniControl.App.Services.Updates;

/// <summary>
/// The update check: asks GitHub's public release list whether a newer version than this one exists, once a day while the switch is on
/// (<see cref="StartDaily"/>) and whenever <see cref="CheckAsync"/> is called. It only checks: it never downloads or installs anything and
/// sends nothing about the person. The state (switch, last check, what it found) is kept in <see cref="UpdateSettings"/>; a failure is
/// logged through <see cref="Guard"/> and shown as one calm line. <see cref="Changed"/> may be raised from any thread.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly UpdateSettings _state;
    private readonly Action _save;
    private readonly Func<CancellationToken, Task<string>> _fetch;
    private readonly Func<DateTime> _now;
    private readonly ReleaseVersion? _current;
    private readonly IDisposable? _owned;
    private readonly CancellationTokenSource _stop = new();
    private bool _started;

    public event Action? Changed;

    public UpdateService(UpdateSettings state, Action save, string currentVersion, Func<CancellationToken, Task<string>> fetch, Func<DateTime> now, IDisposable? owned = null)
    {
        (_state, _save, _fetch, _now, _owned) = (state, save, fetch, now, owned);
        _current = ReleaseVersion.TryParse(currentVersion, out var v) ? v : null;
    }

    /// <summary>The service that asks GitHub, for this version of the program.</summary>
    public static UpdateService Create(AppSettings settings)
    {
        var client = new HttpClient { Timeout = Timing.UpdateRequestTimeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, null)); // generic: no version, nothing personal
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return new UpdateService(settings.Updates, settings.Save, AppInfo.Version, token => client.GetStringAsync(AppInfo.ReleasesApiUrl, token), () => DateTime.Now, client);
    }

    public bool Checking { get; private set; }

    /// <summary>The "Check once a day" switch.</summary>
    public bool Daily
    {
        get => _state.Daily;
        set
        {
            if (_state.Daily == value) return;
            _state.Daily = value;
            _save();
            Changed?.Invoke();
        }
    }

    /// <summary>When the last check ran (answered or not); null: never.</summary>
    public DateTime? LastCheck => _state.LastCheck;

    /// <summary>The last check could not reach GitHub or read its answer.</summary>
    public bool LastFailed => _state.LastFailed;

    /// <summary>The newest release the last successful check found; null: none yet.</summary>
    public ReleaseInfo? Latest => _state.Latest;

    /// <summary>The newest release when it is newer than this version, else null.</summary>
    public ReleaseInfo? Available => _current is { } now && Latest is { } l && ReleaseVersion.TryParse(l.Version, out var v) && v.IsNewerThan(now) ? l : null;

    /// <summary>Starts the daily check: the first one after start-up has settled, then whenever a day has passed since the last.</summary>
    public void StartDaily()
    {
        if (_started) return;
        _started = true;
        _ = RunDailyAsync(_stop.Token);
    }

    private async Task RunDailyAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Timing.UpdateCheckFirstDelay, token);
            while (!token.IsCancellationRequested)
            {
                if (UpdateSchedule.IsDue(_state.Daily, _state.LastCheck, _now(), Timing.UpdateCheckEvery)) await CheckAsync();
                await Task.Delay(Timing.UpdateCheckPoll, token);
            }
        }
        catch (OperationCanceledException)
        {
            // the app is closing
        }
    }

    /// <summary>Asks GitHub now (ignoring the daily throttle); one check at a time.</summary>
    public async Task CheckAsync()
    {
        if (Checking) return;
        Checking = true;
        Changed?.Invoke();
        try
        {
            var newest = UpdateFeed.Newest(await _fetch(_stop.Token));
            _state.Latest = newest ?? _state.Latest;
            _state.LastFailed = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Guard.Log("Update check", ex); // a calm line on screen, the detail here
            _state.LastFailed = true;
        }
        finally
        {
            _state.LastCheck = _now();
            _save();
            Checking = false;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _owned?.Dispose();
    }
}
