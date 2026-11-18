using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Automation;

/// <summary>
/// The automation rules: owns <see cref="AutomationSettings"/> (automation.json), registers every Do rule's own "Run: name" action,
/// every custom action and the built-in power actions in the master list, answers the one gate in <see cref="HotkeyRegistry"/> for the
/// Block rules (<see cref="BlockReason"/>), and starts Do rules from their triggers (<c>AutomationService.Triggers.cs</c>).
/// Safety, all here: a rule never starts again within <see cref="Timing.AutomationMinGap"/>, one that starts too often is paused,
/// Pause all rules (and Ctrl+Alt+Shift+P) stops every rule and block at once, and Shift held while OmniControl starts keeps them all
/// off for that run. Loading never runs anything.
/// </summary>
public sealed partial class AutomationService : IActionGate, IDisposable
{
    private readonly KeepAwakeEntries _keepAwake;

    /// <summary>The page's title: the category every action of this feature is registered under.</summary>
    public const string Category = ActionSections.Automation;

    private const string NewCustomId = "automation.newcustom";
    private const string LogFile = "automation-log.txt";

    private readonly HotkeyRegistry _hotkeys;
    private readonly Action<string> _report;
    private readonly Func<IEnumerable<string>> _remembered;
    private readonly RuleRunner _runner;
    private readonly CustomActionRunner _custom;
    private readonly HotkeyService _emergency = new();
    private readonly Dictionary<string, DateTime> _lastStart = new();              // rule id -> last start (UTC); UI thread only
    private readonly Dictionary<string, Queue<DateTime>> _starts = new();          // recent starts, for the burst limit
    private readonly Dictionary<string, string> _pausedWhy = new();                // rule id -> why it was paused
    private CancellationTokenSource _stopRuns = new();                             // ends running rules (in a wait) when rules are paused
    private readonly Dictionary<string, string> _problems = new();                 // rule id -> what went wrong the last time it ran
    private readonly Dictionary<string, string> _failedShort = new();              // rule id -> that in a few words ("Orbit 11 not reachable")
    private readonly Dictionary<string, int> _tries = new();                       // rule id -> tries its steps needed the last time (more than one)
    private readonly Dictionary<string, string> _waiting = new();                  // rule id -> what its run is waiting for now

    public AutomationSettings Settings { get; } = AutomationSettings.Load();

    /// <summary>Holding Shift while OmniControl started: every rule and block stays off until <see cref="TurnRulesOn"/> (or a restart).</summary>
    public bool SessionOff { get; private set; }

    /// <summary>All rules and blocks are paused (the tray item, the action, the emergency key, the page's button).</summary>
    public bool Paused { get; private set; }

    /// <summary>Raised on the UI thread when something a rule's line shows changed (paused, ran, failed).</summary>
    public event Action? Changed;

    public static string LogPath => AppDataStore.PathOf(LogFile);

    /// <param name="countdown">Shows the cancellable countdown before sleep, shut down and the like (see <see cref="PowerGuard"/>).</param>
    /// <param name="remembered">Programs the Volume page remembers (paths), offered with the running ones.</param>
    /// <param name="startedByWindows">OmniControl was started by its start-up entry: the "At log in" trigger fires.</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    /// <param name="states">The master list of states: the "When a state changes" triggers watch it, and the power states are registered in it.</param>
    /// <param name="power">The power journal: a confirmed wake starts the "wakes from sleep" rules, and it says whether this start-up is a new log-in.</param>
    public AutomationService(HotkeyRegistry hotkeys, StateRegistry states, Func<string, string, TimeSpan, Task<bool>> countdown,
        Func<IEnumerable<string>> remembered, bool startedByWindows, Action<string> report, IPowerEvents power)
    {
        _hotkeys = hotkeys;
        _power = power;
        _states = states;
        _sources = new Sources(hotkeys, states);
        _remembered = remembered;
        _report = report;
        SessionOff = KeyboardState.Held().HasFlag(Modifiers.Shift);
        var actions = new MouseActionRunner(hotkeys.Run, _report, Log);
        _runner = new RuleRunner(actions, IsApproved, Log, id => hotkeys.Find(id)?.Reach);
        _custom = new CustomActionRunner(actions, Log);

        hotkeys.Register(PowerActions.Make(new PowerGuard(countdown, _report), TogglePause));
        _keepAwake = new KeepAwakeEntries(hotkeys, states);
        hotkeys.Register(Settings.Rules.Where(r => r.Kind == AutomationKind.Do).Select(MakeRuleAction));
        hotkeys.Register(Settings.Customs.Select(MakeCustomAction));
        hotkeys.Register(Settings.Scripts.Select(MakeScriptAction));
        hotkeys.RegisterPickerCommand(MakeNewCustomAction());
        hotkeys.Gate = this;

        _emergency.Failed += gesture => _report($"Couldn't register {gesture}, the key that pauses all rules: another program uses it.");
        _emergency.Apply(new[] { (ReservedKeys.Emergency, (Action)(() => Dispatcher.UIThread.Post(TogglePause))) });

        StartLive();
        StartTriggers(startedByWindows);
    }

    // ---- Pausing ----

    /// <summary>Pauses all rules and blocks, or lets them work again. Never blocked, never needs a rule's say.</summary>
    public void TogglePause()
    {
        Paused = !Paused;
        if (Paused)
        {
            _stopRuns.Cancel();
            _stopRuns = new CancellationTokenSource();
        }
        _report(Paused ? "All rules are paused." : "Rules are on again.");
        UpdateWake();
        _states.Notify(PausedId);
        Changed?.Invoke();
    }

    private const string PausedId = "automation.paused";

    /// <summary>"All rules paused" in the master list of states (the tray menu ticks its Pause item from it); setting it pauses or resumes.</summary>
    private StateEntry PausedState() =>
        new(PausedId, "All rules paused", Category, StateKind.OnOff, () => StateValue.Of(Paused), "Paused here, by its action or by the emergency key.",
            Keywords: "pause resume stop automation rules", Set: value => { if (Paused != (value.Number is { } on && on != 0)) TogglePause(); });

    /// <summary>Ends the safe-start state at once: rules and blocks work from now on, with no restart.</summary>
    public void TurnRulesOn()
    {
        if (!SessionOff) return;
        SessionOff = false;
        _report("Rules are on.");
        RunTriggers();
        Changed?.Invoke();
    }

    // ---- Editing ----

    /// <summary>A new rule, off and empty, at the end of the list, with a name of its own.</summary>
    public AutomationRule Add(AutomationKind kind)
    {
        var count = Settings.Rules.Count(r => r.Kind == kind) + 1;
        var rule = new AutomationRule { Kind = kind, Name = kind == AutomationKind.Do ? $"Automation {count}" : $"Block {count}" };
        Settings.Rules.Add(rule);
        if (kind == AutomationKind.Do) _hotkeys.Register(MakeRuleAction(rule));
        Edited(rule);
        return rule;
    }

    public void Remove(AutomationRule rule)
    {
        Settings.Rules.Remove(rule);
        _hotkeys.Unregister(rule.ActionId);
        ForgetRun(rule);
        Edited(rule);
    }

    /// <summary>Call after any change to a rule's blocks or switch: saves, starts its timers again and clears a pause.</summary>
    public void Edited(AutomationRule rule)
    {
        if (!rule.IsComplete) rule.On = false;
        ForgetRun(rule);
        Settings.Save();
        SyncTriggers();
        Changed?.Invoke();
    }

    /// <summary>A setting of a rule that needs no new look at its triggers changed (the seconds of a wait): saved, nothing redrawn.</summary>
    public void Tweaked() => Settings.Save();

    /// <summary>The rule's name changed: its "Run: name" action is made again (its shortcut is kept).</summary>
    public void Renamed(AutomationRule rule)
    {
        if (rule.Kind == AutomationKind.Do)
        {
            _hotkeys.Unregister(rule.ActionId);
            _hotkeys.Register(MakeRuleAction(rule));
        }
        Edited(rule);
    }

    /// <summary>A custom action was made, edited (in the picker or on its tab) or removed, so the other place that shows it follows.</summary>
    public event Action<CustomAction>? CustomAdded, CustomChanged, CustomRemoved;

    /// <summary>Makes a custom action with the given settings (the others at their defaults) and registers it.</summary>
    public CustomAction AddCustom(IReadOnlyDictionary<string, string>? values = null)
    {
        var custom = new CustomAction { Name = NewCustomName() };
        foreach (var (key, value) in values ?? new Dictionary<string, string>()) CustomFields.Apply(custom, key, value);
        Settings.Customs.Add(custom);
        _hotkeys.Register(MakeCustomAction(custom));
        Settings.Save();
        CustomAdded?.Invoke(custom);
        return custom;
    }

    private string NewCustomName() => $"Custom action {Settings.Customs.Count + 1}";

    public void RemoveCustom(CustomAction custom)
    {
        Settings.Customs.Remove(custom);
        _hotkeys.Unregister(custom.ActionId);
        Settings.Save();
        CustomRemoved?.Invoke(custom);
    }

    /// <summary>Stores one setting of a custom action at once; a new name makes its action again (keeping its shortcut). Returns the action's new title when it was renamed, else null.</summary>
    public string? EditCustom(CustomAction custom, string key, string value)
    {
        if (!CustomFields.Apply(custom, key, value)) return null;
        var renamed = key == CustomFields.Name;
        if (renamed)
        {
            _hotkeys.Unregister(custom.ActionId);
            _hotkeys.Register(MakeCustomAction(custom));
        }
        Settings.Save();
        CustomChanged?.Invoke(custom);
        return renamed ? custom.Name : null;
    }

    // ---- What the editor picks from and shows ----

    public HotkeyAction? ActionOf(AutomationRule rule) => _hotkeys.Find(rule.ActionId);

    public IReadOnlyList<PickChoice> ConditionChoices(AutomationRule rule, string? editing) =>
        AutomationConditions.Choices(rule, ProgramNames.Known(_remembered()), editing);

    public IReadOnlyList<PickChoice> StepChoices(AutomationRule rule) =>
        rule.Kind == AutomationKind.Block ? AutomationSteps.BlockChoices(_hotkeys.Actions) : AutomationSteps.DoChoices(_hotkeys.PickerActions);

    /// <summary>What went wrong the last time the rule ran (since OmniControl started), or null.</summary>
    public string? ProblemOf(AutomationRule rule) => _problems.GetValueOrDefault(rule.Id);

    /// <summary>A failed last run in a few words for its tag ("Orbit 11 not reachable"), or null.</summary>
    public string? FailedShortOf(AutomationRule rule) => _failedShort.GetValueOrDefault(rule.Id);

    /// <summary>How many tries the last run's steps needed when more than one, else 1.</summary>
    public int TriesOf(AutomationRule rule) => _tries.GetValueOrDefault(rule.Id, 1);

    /// <summary>What the rule's run is waiting for now (the network and a device, after a wake or a log-in), or null.</summary>
    public string? WaitingFor(AutomationRule rule) => _waiting.GetValueOrDefault(rule.Id);

    /// <summary>The rule starts "At log in" while OmniControl is not started by Windows, so that trigger cannot fire.</summary>
    public bool NeedsStartup(AutomationRule rule) => IsTrigger(rule, AutomationTriggers.Login) && StartupRegistration.State != StartupState.On;

    /// <summary>The fix the "Needs Start with Windows" tag offers: puts OmniControl in Windows' start-up list (as App settings' switch does).</summary>
    public void TurnOnStartup()
    {
        _report(StartupRegistration.Set(true) ? "OmniControl now starts with Windows, so \"At log in\" rules run." : "Windows refused to add OmniControl to its start-up list.");
        Changed?.Invoke();
    }

    /// <summary>Why the rule was paused for starting too often, or null.</summary>
    public string? PausedWhyOf(AutomationRule rule) => _pausedWhy.GetValueOrDefault(rule.Id);

    // ---- Approvals (a program step is allowed once, like a gesture rule's) ----

    public bool IsApproved(string step)
    {
        var hash = CommandApproval.HashOf(step);
        lock (Settings.Approved) return Settings.Approved.Contains(hash);
    }

    public void Approve(string step)
    {
        var hash = CommandApproval.HashOf(step);
        lock (Settings.Approved) Settings.Approved.Add(hash);
        Settings.Save();
    }

    // ---- The gate ----

    /// <summary>
    /// The one question every master action passes before it runs, whatever started it: does a switched-on Block rule forbid it now?
    /// Nothing is blocked while rules are paused or off for the session, and an action that keeps the user in control never is.
    /// </summary>
    public string? BlockReason(HotkeyAction action)
    {
        if (SessionOff || Paused || action.NeverBlock) return null;
        AutomationContext? now = null;
        foreach (var block in Settings.Rules)
        {
            if (block is not { Kind: AutomationKind.Block, On: true } || !block.IsComplete) continue;
            if (!block.Actions.Any(a => AutomationSteps.ActionIdOf(a) == action.Id)) continue;
            now ??= NewContext();
            if (AutomationJoins.Holds(block, now)) return $"{action.Title} is not allowed right now: \"{block.Name}\" blocks it.";
        }
        return null;
    }

    private static AutomationContext NewContext() => new(AutomationWatcher.RunningPrograms);

    // ---- Running ----

    private HotkeyAction MakeRuleAction(AutomationRule rule) =>
        new(rule.ActionId, $"Run {rule.Name}", Category, $"automation rule run schedule timer task scheduler {rule.Name}",
            _ => RunByAction(rule), group: "Rules");

    private HotkeyAction MakeCustomAction(CustomAction custom) =>
        new(custom.ActionId, custom.Name.Length > 0 ? custom.Name : "Custom action", Category,
            $"custom action script command program file url powershell {custom.Name}", ids => _ = RunCustomAsync(custom), group: "Custom actions")
        {
            PickerSection = ActionSections.CustomActions,
            Editor = new ActionEditor(CustomFields.Options, () => CustomFields.Read(custom), (key, value) => EditCustom(custom, key, value)),
            Remove = () => RemoveCustom(custom),
            RemoveText = "Remove custom action",
        };

    /// <summary>The picker's "+ New custom action...": the form that makes one (its settings start at the defaults) and registers it.</summary>
    private HotkeyAction MakeNewCustomAction() =>
        new(NewCustomId, "+ New custom action...", Category, "new add create custom action script command program file url powershell", _ => { })
        {
            PickerSection = ActionSections.CustomActions,
            Editor = new ActionEditor(CustomFields.Options,
                () => new Dictionary<string, string>(CustomFields.Read(new CustomAction { Name = NewCustomName() })),
                Create: values => _hotkeys.Find(AddCustom(values).ActionId)!, CreateText: "Create custom action"),
        };

    /// <summary>Runs a custom action now (its Run pill, or the action itself); one plain sentence in the status line when it failed.</summary>
    public async Task RunCustomAsync(CustomAction custom)
    {
        if (await _custom.RunAsync(custom) is { } problem) _report($"{problem} Details are in {LogFile}.");
    }

    /// <summary>The rule's own action was run (its shortcut, a mouse button, a gesture): it runs its steps if it is on and its conditions hold.</summary>
    private void RunByAction(AutomationRule rule)
    {
        var chain = _hotkeys.Chain; // set when another rule's run (through an "action runs" trigger) runs this rule's own action
        if (!RunChain.MayStart(chain, rule.Id, Timing.AutomationChainMax)) Log($"rule \"{rule.Name}\" not started by its own action: it is already in this chain of rules");
        else if (!rule.On) _report($"\"{rule.Name}\" is switched off.");
        else if (!TryStart(rule, RunChain.After(chain, rule.Id))) _report($"\"{rule.Name}\" did not start: rules are paused, its conditions don't hold, or it just ran.");
    }

    /// <summary>
    /// Starts the rule's steps on a worker unless a safety stops it. False when it did not start. <paramref name="chain"/> is the chain of
    /// rules this run belongs to (this rule last); every action its steps run carries it, so triggers it causes know where they came from.
    /// </summary>
    /// <param name="waitForReady">Started by a wake or a log-in: steps that need the network or a device wait for them first.</param>
    private bool TryStart(AutomationRule rule, RunChain? chain = null, bool waitForReady = false)
    {
        if (!rule.On || !rule.IsComplete || rule.Kind != AutomationKind.Do || Paused || SessionOff || _pausedWhy.ContainsKey(rule.Id)) return false;
        var context = NewContext();
        if (!AutomationJoins.Holds(rule, context)) return false;
        if (ActionOf(rule) is { } own && BlockReason(own) is { } blocked)
        {
            _report(blocked);
            return false;
        }

        var now = DateTime.UtcNow;
        if (_lastStart.TryGetValue(rule.Id, out var last) && now - last < Timing.AutomationMinGap) return false;
        if (!_starts.TryGetValue(rule.Id, out var starts)) _starts[rule.Id] = starts = new Queue<DateTime>();
        while (starts.Count > 0 && now - starts.Peek() > Timing.AutomationBurstWindow) starts.Dequeue();
        if (starts.Count >= Timing.AutomationBurstMax)
        {
            _pausedWhy[rule.Id] = $"It started {Timing.AutomationBurstMax} times in a minute, so it was paused. Switch it off and on again to use it.";
            _report($"\"{rule.Name}\" was paused: it kept starting over and over.");
            Log($"rule \"{rule.Name}\" paused: started {Timing.AutomationBurstMax} times within a minute");
            Changed?.Invoke();
            return false;
        }
        starts.Enqueue(now);
        _lastStart[rule.Id] = now;
        rule.LastRan = DateTime.Now;
        Settings.Save();

        var steps = rule.Actions.ToArray();
        var waits = steps.Select((_, i) => rule.WaitBefore(i)).ToArray();
        var name = rule.Name;
        var id = rule.Id;
        var stop = _stopRuns.Token;
        var runChain = chain ?? RunChain.Start(id);
        var run = new RunReport { WaitForReady = waitForReady, Waiting = target => Dispatcher.UIThread.Post(() => SetWaiting(id, target)) };
        if (waitForReady) Log($"rule \"{name}\" started by a wake or a log-in: steps that need the network or a device wait for them first");
        Task.Run(async () =>
        {
            _hotkeys.RunWithin(runChain);
            var problem = await _runner.RunAsync(name, steps, new MouseEvent(MouseButton.Left, true, 0, 0), i => waits[i], stop, run);
            Dispatcher.UIThread.Post(() => Finished(id, problem, name, run));
        });
        Changed?.Invoke();
        return true;
    }

    private void Finished(string ruleId, string? problem, string name, RunReport run)
    {
        _waiting.Remove(ruleId);
        if (run.Tries > 1) _tries[ruleId] = run.Tries;
        else _tries.Remove(ruleId);
        if (problem is null)
        {
            _problems.Remove(ruleId);
            _failedShort.Remove(ruleId);
        }
        else
        {
            _problems[ruleId] = problem;
            if (run.Short is { } words) _failedShort[ruleId] = words;
            else _failedShort.Remove(ruleId);
            _report($"\"{name}\" didn't finish: {problem}. Details are in {LogFile}.");
        }
        Changed?.Invoke();
    }

    private void SetWaiting(string ruleId, string? target)
    {
        if (target is null) _waiting.Remove(ruleId);
        else _waiting[ruleId] = target;
        Changed?.Invoke();
    }

    private void ForgetRun(AutomationRule rule)
    {
        _pausedWhy.Remove(rule.Id);
        _problems.Remove(rule.Id);
        _failedShort.Remove(rule.Id);
        _tries.Remove(rule.Id);
        _starts.Remove(rule.Id);
        ForgetTimers(rule);
    }

    private static void Log(string line) => AppDataStore.AppendLog(LogFile, line);

    public void Dispose()
    {
        _hotkeys.Gate = null;
        StopLive();
        _stopRuns.Cancel();
        _stopRuns.Dispose();
        StopTriggers();
        _emergency.Dispose();
        _keepAwake.Dispose();
    }
}
