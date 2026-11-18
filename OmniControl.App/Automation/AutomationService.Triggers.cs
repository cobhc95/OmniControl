using Avalonia.Threading;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

// Starting Do rules from their triggers: one look every Timing.AutomationTick at the clock, the idle time, the power source, the
// running programs and the USB devices (AutomationWatcher), plus the start-up events, the confirmed wakes of the power journal
// (IPowerEvents, once per wake) and a wake timer for rules that ask for one.
public sealed partial class AutomationService
{
    private readonly AutomationWatcher _watcher = new();
    private readonly WakeTimer _wake = new();
    private readonly IPowerEvents _power;
    private readonly List<AutomationEvent> _pending = new();       // "At log in", "When OmniControl starts" and wakes, taken by the next look
    private readonly Dictionary<string, DateTime> _timers = new(); // "After a timer" rules: when each started counting (MaxValue: done)
    private readonly HashSet<string> _idleFired = new();           // "idle" rules that already ran in this idle spell
    private DispatcherTimer? _tick;

    /// <summary>The one sentence for the safe-start state (the status line and the report say the same).</summary>
    public const string SafeStartWords = "Rules are off: Shift was held when OmniControl started.";
    private DateTime _lastLook = DateTime.Now;
    private bool _wakeReported;

    private void StartTriggers(bool startedByWindows)
    {
        if (StartupMode.Inert) return; // a screenshot export runs no rule and watches nothing
        if (SessionOff)
        {
            _report(SafeStartWords);
            return;
        }
        _pending.Add(new AutomationEvent(AutomationTriggers.Start));
        if (_power.TakeLogin(startedByWindows)) _pending.Add(new AutomationEvent(AutomationTriggers.Login));
        RunTriggers();
    }

    /// <summary>Starts the clock look and the watchers; at start-up, or when the safe-start state is ended.</summary>
    private void RunTriggers()
    {
        _lastLook = DateTime.Now;
        _power.Woke += OnWoke;
        SyncTriggers();
        _tick = new DispatcherTimer(Timing.AutomationTick, DispatcherPriority.Background, (_, _) => Look());
        _tick.Start();
    }

    /// <summary>The power journal confirmed a wake (any thread): the next look starts the "wakes from sleep" rules.</summary>
    private void OnWoke() => Dispatcher.UIThread.Post(() =>
    {
        Log("the PC woke (confirmed by the power journal)");
        _guard.Woke();
        _pending.Add(new AutomationEvent(AutomationTriggers.Resume));
    });

    private void StopTriggers()
    {
        _power.Woke -= OnWoke;
        _tick?.Stop();
        _watcher.Dispose();
        _wake.Dispose();
    }

    /// <summary>The set of rules changed: watch devices once a rule wants them, and set the wake timer.</summary>
    private void SyncTriggers()
    {
        if (SessionOff) return;
        if (Settings.Rules.Any(r => IsActive(r) && IsTrigger(r, AutomationTriggers.DeviceConnect, AutomationTriggers.DeviceDisconnect))) _watcher.StartDevices();
        UpdateWake();
    }

    private static bool IsActive(AutomationRule rule) =>
        rule is { Kind: AutomationKind.Do, On: true, IsComplete: true } && Watched(rule).Any();

    /// <summary>The triggers the look checks: all but the hotkey, which is the rule's own action.</summary>
    private static IEnumerable<string> Watched(AutomationRule rule) => rule.AllTriggers.Where(t => !AutomationTriggers.IsHotkey(t));

    private static bool IsTrigger(AutomationRule rule, params string[] ids) => Watched(rule).Any(t => ids.Contains(MouseAction.Parse(t).Id));

    /// <summary>Timers and idle spells are counted per trigger: the first trigger by the rule's id, the others with their place added.</summary>
    private static string KeyOf(AutomationRule rule, int place) => place == 0 ? rule.Id : $"{rule.Id}#{place}";

    private void ForgetTimers(AutomationRule rule)
    {
        foreach (var key in _timers.Keys.Where(k => k.StartsWith(rule.Id, StringComparison.Ordinal)).ToList()) _timers.Remove(key);
        _idleFired.RemoveWhere(k => k.StartsWith(rule.Id, StringComparison.Ordinal));
    }

    private void Look()
    {
        var now = DateTime.Now;
        var from = _lastLook;
        _lastLook = now;
        NotifyPowerStates();
        // Taken even while paused: a wake or a start-up is not kept to run hours later when rules are on again.
        var events = new List<AutomationEvent>(_pending);
        _pending.Clear();
        if (Paused) return;

        var afterWake = events.Any(e => e.Kind is AutomationTriggers.Resume or AutomationTriggers.Login or AutomationTriggers.Start);
        var watchPrograms = Settings.Rules.Any(r => IsActive(r) && IsTrigger(r, AutomationTriggers.ProgramStart, AutomationTriggers.ProgramClose));
        events.AddRange(_watcher.Poll(watchPrograms));
        var idle = PowerState.IdleFor();
        if (idle < Timing.AutomationTick) _idleFired.Clear(); // input since the last look: idle rules are ready again

        foreach (var rule in Settings.Rules.Where(IsActive).ToList())
            if (IsDue(rule, events, from, now, idle))
                TryStart(rule, waitForReady: afterWake && IsTrigger(rule, AutomationTriggers.Resume, AutomationTriggers.Login, AutomationTriggers.Start));
        UpdateWake();
    }

    /// <summary>Any one trigger being due starts the rule ("or"); every trigger is asked, so each keeps its own timer.</summary>
    private bool IsDue(AutomationRule rule, IReadOnlyList<AutomationEvent> events, DateTime from, DateTime now, TimeSpan idle)
    {
        var due = false;
        var place = 0;
        foreach (var stored in rule.AllTriggers)
        {
            var key = KeyOf(rule, place++);
            if (!AutomationTriggers.IsHotkey(stored) && IsDue(key, stored, events, from, now, idle)) due = true;
        }
        return due;
    }

    private bool IsDue(string key, string stored, IReadOnlyList<AutomationEvent> events, DateTime from, DateTime now, TimeSpan idle)
    {
        var trigger = MouseAction.Parse(stored);
        switch (trigger.Id)
        {
            case AutomationTriggers.Time:
                return AutomationTriggers.TimeIsDue(stored, from, now, Timing.AutomationCatchUp);
            case AutomationTriggers.Timer:
                if (!_timers.TryGetValue(key, out var counting))
                {
                    _timers[key] = now; // starts counting when the rule is first seen switched on
                    return false;
                }
                if (counting == DateTime.MaxValue || now - counting < TimeSpan.FromMinutes(trigger.Options.Int(AutomationTriggers.Minutes))) return false;
                _timers[key] = trigger.Options.Bool(AutomationTriggers.Repeat) ? now : DateTime.MaxValue;
                return true;
            case AutomationTriggers.Idle:
                if (idle < TimeSpan.FromMinutes(trigger.Options.Int(AutomationTriggers.IdleMinutes)) || _idleFired.Contains(key)) return false;
                _idleFired.Add(key);
                return true;
            case AutomationTriggers.ProgramStart or AutomationTriggers.ProgramClose:
                var program = ProgramNames.Normalize(trigger.Param);
                return events.Any(e => e.Kind == trigger.Id && (program.Length == 0 || e.Name == program));
            case AutomationTriggers.DeviceConnect or AutomationTriggers.DeviceDisconnect:
                var part = trigger.Param.Trim();
                return events.Any(e => e.Kind == trigger.Id && (part.Length == 0 || e.Name.Contains(part, StringComparison.OrdinalIgnoreCase)));
            default:
                return events.Any(e => e.Kind == trigger.Id);
        }
    }

    /// <summary>"Next: Mon 07:30" for a time-of-day rule that is on, or null.</summary>
    public string? NextRunOf(AutomationRule rule) =>
        IsActive(rule) && !Paused && !SessionOff && NextOf(rule, DateTime.Now) is { } next
            ? $"Next: {next:ddd HH:mm}"
            : null;

    private static DateTime? NextOf(AutomationRule rule, DateTime after) =>
        Watched(rule).Select(t => AutomationTriggers.NextTime(t, after)).Where(t => t is not null).Min();

    /// <summary>Wakes the PC for the soonest time-of-day rule that asks for it (Windows can wake a sleeping PC, never one that is off).</summary>
    private void UpdateWake()
    {
        var now = DateTime.Now;
        var next = Paused || SessionOff
            ? null
            : Settings.Rules.Where(IsActive).SelectMany(r => Watched(r).Where(AutomationTriggers.WakesPc))
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
