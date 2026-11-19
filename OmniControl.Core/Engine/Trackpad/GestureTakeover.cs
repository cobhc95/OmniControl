namespace OmniControl.Core.Engine.Trackpad;

/// What OmniControl has done with one of Windows' own touchpad gestures.
public enum TakeoverState
{
    /// Left as Windows has it.
    None,

    /// OmniControl turned it off and will give it back.
    TurnedOff,

    /// OmniControl wanted it off but could not; <see cref="TakeoverStatus.Problem"/> says why.
    Failed,
}

/// <param name="Problem">One plain sentence when <see cref="State"/> is <see cref="TakeoverState.Failed"/>, or when giving the setting back failed.</param>
public sealed record TakeoverStatus(TakeoverState State, string? Problem = null)
{
    public static readonly TakeoverStatus Untouched = new(TakeoverState.None);
}

/// Turns Windows' own touchpad gestures off while an OmniControl action uses the same gesture, and gives them back. Raw input only
/// copies the touchpad's reports, so Windows' own action (search on a three-finger tap, task view on a swipe...) cannot be blocked
/// any other way. Each setting's value from before OmniControl changed it is kept through <c>save</c> (one entry per setting, written
/// before the change and removed only once the value is back), so a crash, a restart or a lost session never loses it: the next
/// <see cref="Update"/> gives back whatever is no longer wanted. A value the user changed meanwhile is never overwritten: only a setting
/// still at OmniControl's "off" is given back. Every write is read back twice: the stored value (a value Windows did not keep counts as
/// refused) and, after Windows was told to use it (<see cref="ITouchpadSettingsStore.Announce"/>), the value Windows acts on; when
/// Windows still acts on its old one the gesture is <see cref="TakeoverState.Failed"/> with the reason, the original stays kept, and
/// every later <see cref="Update"/> and <see cref="Check"/> tells Windows again. Thread-safe: <see cref="StatusOf"/> and
/// <see cref="AnyTurnedOff"/> never wait; every other call touches Windows, so callers keep those off the UI thread.
public sealed class GestureTakeover
{
    private readonly ITouchpadSettingsStore _store;
    private readonly Action<IReadOnlyDictionary<string, int?>> _save;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<WindowsGesture, int?> _originals = new();
    private readonly Dictionary<WindowsGesture, TakeoverStatus> _status = new();
    // Copies for the UI thread, so a read never waits for a write that is announcing itself to every window.
    private volatile IReadOnlyDictionary<WindowsGesture, TakeoverStatus> _published = new Dictionary<WindowsGesture, TakeoverStatus>();
    private volatile bool _anyTurnedOff;
    private readonly Dictionary<WindowsGesture, List<DateTime>> _reapplied = new();

    /// <param name="saved">The originals a previous run kept (keyed by <see cref="WindowsGesture"/> name), or null.</param>
    /// <param name="save">Keeps the originals; called before every change to Windows and after every value given back.</param>
    /// <param name="now">The clock (tests pass their own).</param>
    public GestureTakeover(ITouchpadSettingsStore store, IReadOnlyDictionary<string, int?>? saved, Action<IReadOnlyDictionary<string, int?>> save,
        Func<DateTime>? now = null)
    {
        _store = store;
        _save = save;
        _now = now ?? (() => DateTime.UtcNow);
        foreach (var (name, value) in saved ?? new Dictionary<string, int?>())
        {
            if (!Enum.TryParse<WindowsGesture>(name, out var gesture)) continue; // a name from a newer version: left alone
            _originals[gesture] = value;
            _status[gesture] = new TakeoverStatus(TakeoverState.TurnedOff);
        }
        Publish();
    }

    /// Gets one line per write to Windows (with the value read back) and per change of a gesture's state (for the trackpad log).
    public Action<string>? Log { get; init; }

    /// What OmniControl has done with this Windows gesture.
    public TakeoverStatus StatusOf(WindowsGesture gesture) => _published.GetValueOrDefault(gesture, TakeoverStatus.Untouched);

    /// True while OmniControl keeps at least one Windows gesture off.
    public bool AnyTurnedOff => _anyTurnedOff;

    /// Makes Windows match <paramref name="wanted"/> (the Windows gestures OmniControl's actions replace now): each one that is on is turned
    /// off, each one OmniControl turned off that is no longer wanted is given back. True when any status changed.
    public bool Update(IReadOnlySet<WindowsGesture> wanted)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var setting in WindowsGestureSettings.All)
                changed |= wanted.Contains(setting.Gesture) ? TurnOff(setting) : GiveBack(setting);
            return changed;
        }
    }

    /// Gives back every gesture OmniControl turned off (remapping switched off, exit).
    public bool RestoreAll() => Update(new HashSet<WindowsGesture>());

    /// Turns off again any gesture OmniControl keeps off that something turned back on (the user in Settings, a driver, Windows itself),
    /// and tells Windows again when it still acts on its old value although the stored one is off (the shell's own copy), every time,
    /// but at most once per <see cref="CoreTiming.TrackpadTakeoverReapplyGapMs"/> each (the next look catches it then). Cheap when
    /// nothing changed: a few reads, no write. True when any status changed.
    public bool Check()
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var gesture in _originals.Keys.ToList())
            {
                var setting = WindowsGestureSettings.Of(gesture);
                if (_store.Read(setting, out var value) is not null) continue;
                if (!setting.IsOn(value))
                {
                    changed |= CheckInEffect(setting);
                    continue;
                }
                if (!MayReapply(gesture, out var logIt)) continue;
                var result = WriteAndReadBack(setting, setting.Off);
                if (logIt) Log?.Invoke($"{Describe(setting)} was turned back on ({Show(value)}); turned off again, {result}");
                changed |= Settle(gesture, result);
            }
            return changed;
        }
    }

    /// The stored value is off: when Windows still acts on its old one, it is told again (rate-limited like a re-apply).
    private bool CheckInEffect(WindowsGestureSetting setting)
    {
        var gesture = setting.Gesture;
        if (_store.ReadInEffect(setting, out var used) is not null) return false; // the shell is restarting or busy: the next look tries again
        if (!setting.IsOn(used))
            return _status.GetValueOrDefault(gesture)?.State == TakeoverState.Failed && SetStatus(gesture, new TakeoverStatus(TakeoverState.TurnedOff));
        if (!MayReapply(gesture, out var logIt)) return false;
        return SetStatus(gesture, StatusAfter(EnsureInEffect(setting, setting.Off, logIt)));
    }

    private bool TurnOff(WindowsGestureSetting setting)
    {
        var gesture = setting.Gesture;
        // A refused write is not tried again until the gesture stops being wanted; a stored "off" that Windows does not act on yet is.
        if (_status.GetValueOrDefault(gesture)?.State == TakeoverState.Failed && !_originals.ContainsKey(gesture)) return false;

        if (_store.Read(setting, out var value) is { } readProblem) return SetStatus(gesture, new TakeoverStatus(TakeoverState.Failed, readProblem));
        if (!setting.IsOn(value))
        {
            // Off as stored: OmniControl's (this run, or a crashed one), or the user's own choice. Either way Windows must act on it.
            var problem = EnsureInEffect(setting, value);
            return SetStatus(gesture, _originals.ContainsKey(gesture) ? StatusAfter(problem) : TakeoverStatus.Untouched);
        }
        if (_originals.ContainsKey(gesture))
        {
            var again = WriteAndReadBack(setting, setting.Off);
            Log?.Invoke($"{Describe(setting)} was back on ({Show(value)}); turned off again, {again}");
            return Settle(gesture, again);
        }

        _originals[gesture] = value;
        Persist(); // the original is kept before Windows is touched
        var result = WriteAndReadBack(setting, setting.Off);
        Log?.Invoke($"{Describe(setting)} was {Show(value)}; set to {setting.Off}, {result}");
        if (result.Stored) _reapplied.Remove(gesture);
        return Settle(gesture, result);
    }

    private bool GiveBack(WindowsGestureSetting setting)
    {
        var gesture = setting.Gesture;
        if (!_originals.TryGetValue(gesture, out var original)) return SetStatus(gesture, TakeoverStatus.Untouched);

        if (_store.Read(setting, out var value) is { } readProblem)
            return SetStatus(gesture, new TakeoverStatus(TakeoverState.TurnedOff, readProblem)); // kept: tried again next time
        // Still at OmniControl's "off": give the original back. Anything else is a newer choice of the user's, which stays.
        if (!setting.IsOn(value) && value != original)
        {
            var result = WriteAndReadBack(setting, original);
            Log?.Invoke($"{Describe(setting)} given back: set to {Show(original)}, {result}");
            if (!result.Stored)
                return SetStatus(gesture, new TakeoverStatus(TakeoverState.TurnedOff, $"OmniControl couldn't turn Windows' {setting.Name} back on: {result.Problem}"));
            // Stored but not yet acted on: the shell reads it at its next start (sign-in or an Explorer restart); nothing more to keep.
        }
        else Log?.Invoke($"{Describe(setting)} left as it is ({Show(value)}): the user changed it meanwhile");
        Forget(gesture);
        return SetStatus(gesture, TakeoverStatus.Untouched);
    }

    /// What one write did: whether the value is stored, what reads back, what Windows acts on afterwards, and why it fell short.
    private sealed record WriteResult(bool Stored, int? ReadBack, int? InEffect, string? Problem)
    {
        public override string ToString() =>
            !Stored && Problem is not null ? $"refused ({Problem})"
            : $"reads back {Show(ReadBack)}, Windows acts on {Show(InEffect)}{(Problem is null ? "" : $" ({Problem})")}";
    }

    /// Writes <paramref name="value"/>, reads it back (a value Windows did not keep is refused), tells Windows to act on it and reads what
    /// Windows acts on (still the old one = a problem, though the value stays stored).
    private WriteResult WriteAndReadBack(WindowsGestureSetting setting, int? value)
    {
        if (_store.Write(setting, value) is { } problem) return new WriteResult(false, null, null, problem);
        if (_store.Read(setting, out var readBack) is { } readProblem) return new WriteResult(false, null, null, readProblem);
        if (setting.IsOn(readBack) != setting.IsOn(value)) return new WriteResult(false, readBack, null, $"Windows didn't keep the change to its {setting.Name}.");
        if (_store.Announce(setting) is { } announceProblem) return new WriteResult(true, readBack, null, announceProblem);
        if (_store.ReadInEffect(setting, out var used) is { } usedProblem) return new WriteResult(true, readBack, null, usedProblem);
        return new WriteResult(true, readBack, used, setting.IsOn(used) == setting.IsOn(value) ? null : StillActs(setting, used));
    }

    /// Makes Windows act on the stored <paramref name="stored"/>: when what it acts on differs, it is told to read the setting again and
    /// asked once more. Null when Windows acts on the stored value, or one plain sentence. <paramref name="logIt"/> false = the log already
    /// had its lines for this gesture this minute.
    private string? EnsureInEffect(WindowsGestureSetting setting, int? stored, bool logIt = true)
    {
        if (_store.ReadInEffect(setting, out var used) is { } problem) return problem;
        if (setting.IsOn(used) == setting.IsOn(stored)) return null;
        var announced = _store.Announce(setting);
        var reread = _store.ReadInEffect(setting, out var after);
        if (logIt)
            Log?.Invoke($"{Describe(setting)} is {Show(stored)} but Windows still acted on {Show(used)}; told it to read the setting again, "
                        + $"now acts on {(reread is null ? Show(after) : "an unknown value")}");
        if (announced is not null) return announced;
        if (reread is not null) return reread;
        return setting.IsOn(after) == setting.IsOn(stored) ? null : StillActs(setting, after);
    }

    private static string StillActs(WindowsGestureSetting setting, int? used) =>
        $"Windows still acts on its old {setting.Name} setting ({Show(used)}) and didn't take the new one.";

    private static TakeoverStatus StatusAfter(string? problem) =>
        problem is null ? new TakeoverStatus(TakeoverState.TurnedOff) : new TakeoverStatus(TakeoverState.Failed, problem);

    /// The status after a write to "off": refused = nothing changed (the original is forgotten); stored = OmniControl's, with any problem.
    private bool Settle(WindowsGesture gesture, WriteResult result) =>
        result.Stored ? SetStatus(gesture, StatusAfter(result.Problem)) : Fail(gesture, result.Problem!);

    private bool Fail(WindowsGesture gesture, string problem)
    {
        Forget(gesture); // nothing was changed, so nothing is to be given back
        return SetStatus(gesture, new TakeoverStatus(TakeoverState.Failed, problem));
    }

    private void Forget(WindowsGesture gesture)
    {
        _reapplied.Remove(gesture);
        if (_originals.Remove(gesture)) Persist();
    }

    /// True when this gesture may be turned off again now; <paramref name="logIt"/> says whether the log still takes a line for it.
    private bool MayReapply(WindowsGesture gesture, out bool logIt)
    {
        var now = _now();
        if (!_reapplied.TryGetValue(gesture, out var times)) _reapplied[gesture] = times = new List<DateTime>();
        times.RemoveAll(t => (now - t).TotalMilliseconds > CoreTiming.TrackpadTakeoverReapplyLogWindowMs);
        logIt = times.Count < CoreTiming.TrackpadTakeoverReapplyLogLimit;
        if (times.Count > 0 && (now - times[^1]).TotalMilliseconds < CoreTiming.TrackpadTakeoverReapplyGapMs) return false;
        times.Add(now);
        return true;
    }

    private bool SetStatus(WindowsGesture gesture, TakeoverStatus status)
    {
        var before = _status.GetValueOrDefault(gesture, TakeoverStatus.Untouched);
        if (status == TakeoverStatus.Untouched) _status.Remove(gesture);
        else _status[gesture] = status;
        Publish();
        if (before == status) return false;
        Log?.Invoke($"Windows' {WindowsGestureSettings.Of(gesture).Name}: {before.State} -> {status.State}{(status.Problem is null ? "" : $" ({status.Problem})")}");
        return true;
    }

    private static string Describe(WindowsGestureSetting setting) => $"Windows' {setting.Name} ({setting.RegistryValue})";

    /// A raw value for the log: the number, or "missing" (Windows' default applies).
    public static string Show(int? value) => value?.ToString() ?? "missing";

    private void Publish()
    {
        _published = new Dictionary<WindowsGesture, TakeoverStatus>(_status);
        _anyTurnedOff = _originals.Count > 0;
    }

    private void Persist() => _save(_originals.ToDictionary(p => p.Key.ToString(), p => p.Value));
}
