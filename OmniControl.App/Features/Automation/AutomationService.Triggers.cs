using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

// Starting Do rules from their triggers: one look every Timing.AutomationTick at the clock, the idle time, the power source, the
// running programs (read for the program states, ProgramStates) and the USB devices (AutomationWatcher), plus the start-up events, the confirmed wakes of the power journal
// (IPowerEvents, once per wake) and a wake timer for rules that ask for one.
public sealed partial class AutomationService
{
    private readonly AutomationWatcher _watcher = new();
    private readonly WakeTimer _wake = new();
    private readonly IPowerEvents _power;
    private readonly List<AutomationEvent> _pending = new();       // "At log in", "When OmniControl starts" and wakes, taken by the next look
    private readonly Dictionary<string, DateTime> _timers = new(); // "After a timer" rules: when each started counting (MaxValue: done)
    private double? _idleMinutes;
    private DispatcherTimer? _tick;

    /// The one sentence for the safe-start state (the status line and the report say the same).
    public const string SafeStartWords = "Rules are off: Shift was held when OmniControl started.";
    private DateTime _lastLook = DateTime.Now;
    private bool _wakeReported;

    private void StartTriggers(bool startedByWindows)
    {
        if (SessionOff)
        {
            _report(SafeStartWords);
            return;
        }
        _pending.Add(new AutomationEvent(AutomationTriggers.Start));
        if (_power.TakeLogin(startedByWindows)) _pending.Add(new AutomationEvent(AutomationTriggers.Login));
        RunTriggers();
    }

    /// Starts the clock look and the watchers; at start-up, or when the safe-start state is ended.
    private void RunTriggers()
    {
        _lastLook = DateTime.Now;
        _power.Woke += OnWoke;
        _watcher.EventQueued += OnWatcherEvent;
        _states.WatchedChanged += SyncTriggers;
        _tick = new DispatcherTimer(Timing.AutomationTick, DispatcherPriority.Background, (_, _) => Look());
        SyncTriggers();
        if (_pending.Count > 0) Dispatcher.UIThread.Post(Look, DispatcherPriority.Background);
    }

    private void OnWatcherEvent() => Dispatcher.UIThread.Post(Look, DispatcherPriority.Background);

    /// The hardware watch "USB devices (rules)": Windows announced a device change and the burst has settled (worker thread).
    internal void LookAtUsb() => _watcher.LookAtUsb();

    /// The power journal confirmed a wake (any thread): the next look starts the "wakes from sleep" rules.
    private void OnWoke() => Dispatcher.UIThread.Post(() =>
    {
        Log("the PC woke (confirmed by the power journal)");
        _guard.Woke();
        _pending.Add(new AutomationEvent(AutomationTriggers.Resume));
        Look();
    });

    private void StopTriggers()
    {
        _power.Woke -= OnWoke;
        _watcher.EventQueued -= OnWatcherEvent;
        _states.WatchedChanged -= SyncTriggers;
        _tick?.Stop();
        _watcher.Dispose();
        _wake.Dispose();
    }

    /// True when a rule has a trigger that requires clock polling (time of day, time window, repeating/one-shot timer);
    /// state, action, mouse-button, USB, login and wake triggers are event-driven and need no timer.
    private static bool NeedsClock(AutomationRule rule) =>
        Watched(rule).Any(t => MouseAction.Parse(t).Id is AutomationTriggers.Time or AutomationTriggers.Between or AutomationTriggers.Timer);

    /// The set of rules changed: watch devices once a rule wants them, and run the clock tick only when a polled trigger or watched power/idle state needs it.
    private void SyncTriggers()
    {
        SyncPrograms();
        if (SessionOff) return;
        ShareWatched();
        var needsClock = _programs.Watching
            || Settings.Rules.Any(r => (Watches(r) && NeedsClock(r)) || (AsksPrograms(r) && !Stopped(r)))
            || _states.IsWatched(AutomationTriggers.IdleStateId)
            || PowerStates.Ids.Any(_states.IsWatched)
            || ValuesShown is not null;
        if (_tick is not null)
        {
            _tick.Interval = Timing.AutomationTick;
            _tick.IsEnabled = needsClock;
        }
        LookAtHeld();
        UpdateWake();
    }

    internal static bool IsActive(AutomationRule rule) =>
        rule is { Kind: AutomationKind.Do, On: true, IsComplete: true } && Watched(rule).Any();

    /// The triggers the look checks: all but the hotkey, which is the rule's own action.
    private static IEnumerable<string> Watched(AutomationRule rule) => rule.AllTriggers.Where(t => !AutomationTriggers.IsHotkey(t));

    private static bool IsTrigger(AutomationRule rule, params string[] ids) => Watched(rule).Any(t => ids.Contains(MouseAction.Parse(t).Id));

    /// Timers are counted per trigger: the first trigger by the rule's id, the others with their place added.
    private static string KeyOf(AutomationRule rule, int place) => place == 0 ? rule.Id : $"{rule.Id}#{place}";

    private void ForgetTimers(AutomationRule rule)
    {
        foreach (var key in _timers.Keys.Where(k => k.StartsWith(rule.Id, StringComparison.Ordinal)).ToList()) _timers.Remove(key);
    }

    /// One look (UI thread). With a program trigger or a "program is running" condition on, the running programs are listed on a worker first
    /// (a slow call on a busy PC) and the look goes on when the list is back; a tick that comes while a list is still being read is skipped.
    private void Look()
    {
        if (_readingPrograms) return;
        if (!_programs.Watching && !Settings.Rules.Any(r => AsksPrograms(r) && !Stopped(r)))
        {
            Guard.Run("Automation look", LookWith);
            return;
        }
        _readingPrograms = true;
        Guard.OffUi("Automation: reading the running programs", () =>
        {
            HashSet<string>? programs = null;
            Guard.Run("Automation: reading the running programs", () => programs = AutomationWatcher.RunningPrograms());
            Dispatcher.UIThread.Post(() =>
            {
                _readingPrograms = false;
                if (programs is not null)
                {
                    _programsSeen = programs;
                    _programs.Saw(programs);
                }
                if (_tick?.IsEnabled == true || _pending.Count > 0) Guard.Run("Automation look", LookWith);
            });
        });
    }

    private bool _readingPrograms;

    /// The running programs the last look read, which conditions asked on the UI thread use (never a process list there).
    private IReadOnlySet<string> _programsSeen = new HashSet<string>();

    /// True for a switched-on rule with a "program is running" condition.
    private static bool AsksPrograms(AutomationRule rule) =>
        rule is { On: true, IsComplete: true } && rule.Conditions.Any(c => MouseAction.Parse(c).Id == AutomationConditions.Running);

    /// What the conditions are asked about now: on the UI thread with the programs the last look read, elsewhere (the gate) listed then.
    private AutomationContext NewContext() =>
        new(() => Dispatcher.UIThread.CheckAccess() ? _programsSeen : AutomationWatcher.RunningPrograms());

    private void LookWith()
    {
        var now = DateTime.Now;
        var from = _lastLook;
        _lastLook = now;
        NotifyPowerStates();
        if (_states.IsWatched(AutomationTriggers.IdleStateId) || ValuesShown is not null)
        {
            _idleMinutes = Math.Floor(PowerState.IdleFor().TotalMinutes);
            _states.Notify(AutomationTriggers.IdleStateId);
        }
        // Taken even while paused: a wake or a start-up is not kept to run hours later when rules are on again.
        var events = new List<AutomationEvent>(_pending);
        _pending.Clear();
        if (!Settings.Rules.Any(Needs)) return;

        var afterWake = events.Any(e => e.Kind is AutomationTriggers.Resume or AutomationTriggers.Login or AutomationTriggers.Start);
        events.AddRange(_watcher.Poll());
        foreach (var rule in Settings.Rules.Where(r => Needs(r) && (!IsWhile(r) || r.Feature.Length > 0)).ToList())
        {
            var waitForReady = afterWake && IsTrigger(rule, AutomationTriggers.Resume, AutomationTriggers.Login, AutomationTriggers.Start);
            if (!HasOtherwise(rule) || IsWhile(rule))
            {
                if (IsDue(rule, events, from, now)) TryStart(rule, waitForReady: waitForReady);
                continue;
            }
            var due = DuePlaces(rule, events, from, now).ToList();
            if (due.Count == 0) continue;
            var context = NewContext();
            if (due.Any(i => TriggersHold(rule, i) && (rule.Conditions.Count == 0 || AutomationJoins.Holds(rule, context)))) TryStart(rule, waitForReady: waitForReady);
            else TryStart(rule, waitForReady: waitForReady, otherwise: true);
        }
        LookAtHeld();
        UpdateWake();
    }

    /// Every trigger is asked so each keeps its own timer; the rule is due when a trigger is due and the joined triggers hold with it.
    private bool IsDue(AutomationRule rule, IReadOnlyList<AutomationEvent> events, DateTime from, DateTime now) =>
        DuePlaces(rule, events, from, now).Any(i => TriggersHold(rule, i));

    /// The places of the rule's triggers that are due now; every trigger is asked, so each keeps its own timer.
    private IEnumerable<int> DuePlaces(AutomationRule rule, IReadOnlyList<AutomationEvent> events, DateTime from, DateTime now)
    {
        var place = 0;
        foreach (var stored in rule.AllTriggers)
        {
            var index = place++;
            var key = KeyOf(rule, index);
            if (!AutomationTriggers.IsHotkey(stored) && IsDue(key, stored, events, from, now)) yield return index;
        }
    }

    private bool IsDue(string key, string stored, IReadOnlyList<AutomationEvent> events, DateTime from, DateTime now)
    {
        var trigger = MouseAction.Parse(stored);
        switch (trigger.Id)
        {
            case AutomationTriggers.Time:
                return AutomationTriggers.TimeIsDue(stored, from, now, Timing.AutomationCatchUp);
            case AutomationTriggers.Between:
                return AutomationTriggers.WindowBegins(stored, from, now);
            case AutomationTriggers.Timer:
                if (!_timers.TryGetValue(key, out var counting))
                {
                    _timers[key] = now; // starts counting when the rule is first seen switched on
                    return false;
                }
                if (counting == DateTime.MaxValue || now - counting < TimeSpan.FromMinutes(trigger.Options.Int(AutomationTriggers.Minutes))) return false;
                _timers[key] = trigger.Options.Bool(AutomationTriggers.Repeat) ? now : DateTime.MaxValue;
                return true;
            case AutomationTriggers.DeviceConnect or AutomationTriggers.DeviceDisconnect:
                var part = trigger.Param.Trim();
                return events.Any(e => e.Kind == trigger.Id && (part.Length == 0 || e.Name.Contains(part, StringComparison.OrdinalIgnoreCase)));
            default:
                return events.Any(e => e.Kind == trigger.Id);
        }
    }

    /// "Next: Mon 07:30" for a time-of-day rule that is on, or null.
    public string? NextRunOf(AutomationRule rule) =>
        Needs(rule) && NextOf(rule, DateTime.Now) is { } next
            ? $"Next: {next:ddd HH:mm}"
            : null;

    private static DateTime? NextOf(AutomationRule rule, DateTime after) =>
        Watched(rule).Select(t => AutomationTriggers.NextTime(t, after)).Where(t => t is not null).Min();

    /// Wakes the PC for the soonest time-of-day rule that asks for it (Windows can wake a sleeping PC, never one that is off).
    private void UpdateWake()
    {
        var now = DateTime.Now;
        var next = Settings.Rules.Where(Needs).SelectMany(r => Watched(r).Where(AutomationTriggers.WakesPc))
                .Select(t => AutomationTriggers.NextTime(t, now)).Where(t => t is not null).Min();
        if (next is null)
        {
            _wake.Cancel();
            return;
        }
        if (_wake.Set(next.Value) || _wakeReported) return;
        _wakeReported = true;
        _report("Windows would not set a wake timer, so the PC will not wake for a rule. Check that wake timers are allowed in the power plan.");
    }
}
