using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Rules;

/// The custom gesture rules: owns <see cref="RuleSettings"/> (rules.json), hands the on rules to the <see cref="RuleEngine"/> and
/// their shortcuts to Windows, runs them (<see cref="RuleRunner"/>), keeps each rule's last problem, and answers the editor's
/// questions (the lists to pick from, the sentence, conflicts, approvals). Loading never runs anything: a rule runs only from its
/// trigger or the editor's Test. Rules follow the "Enable gestures" switch and edge strip.
public sealed class RuleService : IDisposable
{
    private const string LogFile = "rules-log.txt";

    private readonly GestureService _gestures;
    private readonly MouseService _mouse;
    private readonly IActionCatalogue _hotkeys;
    private readonly Func<IEnumerable<string>> _remembered;
    private readonly Action<string> _report;
    private readonly RuleEngine _engine;
    private readonly RuleRunner _runner;
    private readonly HotkeyService _shortcuts = new();
    private readonly Dictionary<string, string> _problems = new(); // rule id -> what went wrong last time (UI thread)

    public RuleSettings Settings { get; } = RuleSettings.Load();

    /// Raised on the UI thread when a rule's last run went wrong or right (its problem chip changes).
    public event Action? ProblemsChanged;

    /// Where failures are written, for the editor's "Copy details".
    public static string LogPath => AppDataStore.PathOf(LogFile);

    /// <param name="hook">The shared mouse hook; rules are asked before the gestures and the mouse mappings.</param>
    /// <param name="remembered">Programs the Volume page remembers (paths), offered in the program condition with the running ones.</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    public RuleService(MouseHook hook, GestureService gestures, MouseService mouse, IActionCatalogue hotkeys, IActionRunner run,
        Func<IEnumerable<string>> remembered, Action<string> report, Func<DialogMessage, Task<bool>> ask)
    {
        _gestures = gestures;
        _mouse = mouse;
        _hotkeys = hotkeys;
        _remembered = remembered;
        _report = report;
        var actions = new MouseActionRunner(run.Run, report, Log);
        Approvals = new CommandApprovals(() => Settings.Approved, Settings.Save, ask);
        _runner = new RuleRunner(actions, Approvals.IsApproved, Log);
        _engine = new RuleEngine(hook, mouse.LayerOf, Start);
        _shortcuts.Failed += shortcut =>
            report($"Couldn't use {shortcut} for a rule: another program or an OmniControl shortcut already uses it.");
        gestures.Applied += Load;
        hotkeys.Moved += FollowMoved;
        Load();
    }

    /// A rule step whose action's id moved (a merged smart device's actions) now names the new id.
    private void FollowMoved(IReadOnlyDictionary<string, string> moved)
    {
        var changed = Settings.Rules.Aggregate(false, (any, rule) => MovedActions.RewriteAll(rule.Steps, moved) | any);
        if (changed) Apply();
    }

    /// Call after any change to a rule: saves, and hands the hook and Windows a fresh copy.
    public void Apply()
    {
        Settings.Save();
        Load();
    }

    private void Load()
    {
        var compiled = Settings.Rules.Select(CompiledRule.Compile).OfType<CompiledRule>().ToList();
        _engine.Load(compiled, _gestures.Settings.Enabled, _gestures.Settings.ClampedEdgeStrip());
        var shortcuts = _gestures.Settings.Enabled ? compiled.Where(r => r.Shortcut.Length > 0) : Enumerable.Empty<CompiledRule>();
        _shortcuts.Apply(shortcuts.Select(r => (r.Shortcut, (Action)(() => _engine.OnShortcut(r)))));
        Loaded?.Invoke();
    }

    /// The rules the hook and the touchpad watcher follow were handed over again (UI thread): what they need of Windows' input may have changed.
    public event Action? Loaded;

    /// A rule that is on listens to a trackpad gesture (any thread).
    public bool NeedsTrackpad => _engine.NeedsTrackpad;

    /// A new rule, off and empty, at the end of the list.
    public Rule Add()
    {
        var rule = new Rule();
        Settings.Rules.Add(rule);
        Apply();
        return rule;
    }

    /// Puts <paramref name="target"/>'s rules and approved commands in place of the current ones ("Restore defaults" and its undo).
    public void ReplaceAll(RuleSettings target)
    {
        Settings.Rules.Clear();
        Settings.Rules.AddRange(target.Rules);
        Settings.Approved.Clear();
        Settings.Approved.UnionWith(target.Approved);
        _problems.Clear();
        Apply();
    }

    /// Takes rules.json from disk as it is now (an import wrote it) and applies it like any change.
    public bool ReloadFromDisk()
    {
        ReplaceAll(RuleSettings.Load());
        return true;
    }

    public void Remove(Rule rule)
    {
        Settings.Rules.Remove(rule);
        _problems.Remove(rule.Id);
        Apply();
    }

    // ---- What the editor picks from (read from the master lists each time) ----

    public IReadOnlyList<PickChoice> TriggerChoices() => RuleTriggers.Choices();

    /// <param name="editing">The stored condition being changed, or null for a new one.</param>
    public IReadOnlyList<PickChoice> ConditionChoices(Rule rule, string? editing) =>
        RuleConditions.Choices(rule, ProgramNames.Known(_remembered()), editing);

    public IReadOnlyList<PickChoice> StepChoices() => RuleSteps.Choices(_hotkeys.PickerActions);

    public string SentenceOf(Rule rule) => RuleSentence.Of(rule, StepChoices());

    public RuleConflict? ConflictOf(Rule rule) => RuleConflicts.Of(rule, _gestures.Settings, _mouse.Settings);

    /// What went wrong the last time the rule ran (since OmniControl started), or null.
    public string? ProblemOf(Rule rule) => _problems.GetValueOrDefault(rule.Id);

    /// The layer drop-down: "Any layer", then every layer by name, in order (index = layer number); the trackpad's layers for a rule with a
    /// trackpad trigger, the mouse's for any other (<see cref="RuleTrigger.Family"/>).
    public IReadOnlyList<string> LayerNames(Rule rule)
    {
        var labels = _mouse.Settings.LayersOf(RuleTriggers.FamilyOf(rule.Trigger)).Labels();
        return new[] { AnyLayerName }.Concat(Enumerable.Range(1, MouseSettings.Layers).Select(n => labels[n])).ToList();
    }

    public const string AnyLayerName = "Any layer";

    // ---- Approvals ----

    /// The commands and programs the person allowed on this PC.
    public CommandApprovals Approvals { get; }

    // ---- Running ----

    /// The editor's Test: runs the rule's steps once now (where the pointer is), whatever its trigger, conditions and switch say.
    public void Test(Rule rule)
    {
        var (x, y) = SystemCommands.GetCursorPos() ?? (0, 0);
        var steps = rule.Steps.ToArray();
        var name = rule.Name.Trim() is { Length: > 0 } named ? named : CompiledRule.UnnamedRule;
        Guard.OffUi("Gesture rules: testing a rule", async () => Finish(rule.Id, await _runner.RunAsync(name, steps, new MouseEvent(MouseButton.Left, true, x, y))));
    }

    /// Called on the hook or hotkey thread for a matching rule: starts its steps on a worker unless its rate limit holds it back.
    private void Start(CompiledRule rule, MouseEvent at)
    {
        if (!rule.TryStart()) return;
        Guard.OffUi("Gesture rules: running a rule", async () =>
        {
            try
            {
                Finish(rule.Id, await _runner.RunAsync(rule.Name, rule.Steps, at));
            }
            finally
            {
                rule.Finished();
            }
        });
    }

    private void Finish(string ruleId, string? problem) => Dispatcher.UIThread.Post(() =>
    {
        var had = _problems.GetValueOrDefault(ruleId);
        if (problem is null) _problems.Remove(ruleId);
        else
        {
            _problems[ruleId] = problem;
            _report(Guard.SeeLog($"A rule didn't finish: {problem}.", LogFile));
        }
        if (had != problem) ProblemsChanged?.Invoke();
    });

    private static void Log(string line) => AppDataStore.AppendLog(LogFile, line);

    public void Dispose()
    {
        _gestures.Applied -= Load;
        _hotkeys.Moved -= FollowMoved;
        _shortcuts.Dispose();
        _engine.Dispose();
    }
}
