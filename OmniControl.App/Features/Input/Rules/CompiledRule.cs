using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Rules;

/// What the engine needs to know about the place and moment of one input, worked out only when a rule asks and at most once per input
/// (the area test can ask a window a question, so it is the last thing checked). A struct: asking costs no allocation.
internal struct RuleProbe
{
    private readonly int _x, _y;
    private readonly bool _underPointer; // a mouse trigger looks at the window under the pointer; a shortcut at the active window
    private PointerAreaKind? _area;
    private Modifiers? _held;
    private string? _program;
    private int? _layer;

    public RuleProbe(int x, int y, bool underPointer)
    {
        _x = x;
        _y = y;
        _underPointer = underPointer;
        _area = null;
        _held = null;
        _program = null;
        _layer = null;
    }

    /// The layer of <paramref name="family"/> in force (every rule asked about one input has the same family).
    public int Layer(Func<InputFamily, int> current, InputFamily family) => _layer ??= current(family);

    public Modifiers Held => _held ??= KeyboardState.Held();

    public PointerAreaKind Area(int edgeStrip) => _area ??= PointerArea.At(_x, _y, edgeStrip);

    public string Program(ProgramNames names) =>
        _program ??= names.NameOf(_underPointer ? WindowQuery.ProcessIdAt(_x, _y) : WindowQuery.ActiveProcessId());
}

/// One rule made ready for the hook: its trigger, layer and conditions as plain arrays, its rate limit, and its steps. Built by
/// <see cref="Compile"/> on the UI thread; the hook thread only reads it, except for the run bookkeeping (<see cref="TryStart"/>).
internal sealed class CompiledRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required RuleTrigger Trigger { get; init; }
    public required string Shortcut { get; init; }
    public required int HoldMs { get; init; }
    public required IReadOnlyList<string> Steps { get; init; }
    private int Layer { get; init; }
    private InputFamily Family { get; init; }
    private bool AnyOf { get; init; }
    private long RateLimitMs { get; init; }
    private PointerAreaKind[] Areas { get; init; } = Array.Empty<PointerAreaKind>();
    private string[] Programs { get; init; } = Array.Empty<string>();
    private Modifiers[] Keys { get; init; } = Array.Empty<Modifiers>();

    /// What the log calls a rule the user did not name.
    public const string UnnamedRule = "a rule without a name";

    private long _lastRun = long.MinValue / 2;
    private int _running;

    /// The rule ready to run, or null when it is off, incomplete, or holds something this version can't read.
    public static CompiledRule? Compile(Rule rule)
    {
        if (!rule.On || !rule.IsComplete || RuleTriggers.Of(rule.Trigger) is not { } trigger || RuleTriggers.ProblemOf(rule.Trigger) is not null) return null;
        var areas = rule.Conditions.Select(RuleConditions.AreaOf).OfType<PointerAreaKind>().ToArray();
        var programs = rule.Conditions.Select(RuleConditions.ProgramOf).OfType<string>().ToArray();
        var keys = rule.Conditions.Select(RuleConditions.KeysOf).OfType<Modifiers>().ToArray();
        if (areas.Length + programs.Length + keys.Length != rule.Conditions.Count) return null;

        var parsed = MouseAction.Parse(rule.Trigger);
        return new CompiledRule
        {
            Id = rule.Id,
            Name = rule.Name.Trim() is { Length: > 0 } name ? name : UnnamedRule,
            Trigger = trigger,
            Shortcut = trigger.Kind == TriggerKind.Shortcut ? parsed.Param.Trim() : string.Empty,
            HoldMs = parsed.Options.Int(RuleTriggers.HoldTime),
            Steps = rule.Steps.ToArray(),
            Layer = rule.Layer,
            Family = trigger.Family,
            AnyOf = rule.AnyOf,
            RateLimitMs = (long)rule.ClampedRateLimit().TotalMilliseconds,
            Areas = areas,
            Programs = programs,
            Keys = keys,
        };
    }

    /// True when the rule applies to this input: its layer, then its conditions (all, or any one). A rule without a "keys held"
    /// condition does not apply while a modifier key is held (as for gestures), except to a shortcut, whose keys are its own.
    public bool Applies(ref RuleProbe probe, Func<InputFamily, int> layer, int edgeStrip, ProgramNames names)
    {
        if (Layer != Rule.AnyLayer && Layer != probe.Layer(layer, Family)) return false;
        if (Trigger.Kind != TriggerKind.Shortcut && Keys.Length == 0 && probe.Held != Modifiers.None) return false;
        if (Keys.Length + Programs.Length + Areas.Length == 0) return true;

        // Cheapest first: held keys, then the (cached) program, then the area, which may ask a window a question.
        foreach (var keys in Keys)
            if (Decided(probe.Held == keys, out var result)) return result;
        foreach (var program in Programs)
            if (Decided(string.Equals(probe.Program(names), program, StringComparison.Ordinal), out var result)) return result;
        foreach (var area in Areas)
            if (Decided(probe.Area(edgeStrip) == area, out var result)) return result;
        return !AnyOf; // all held (and), or none did (or)
    }

    /// One condition's answer settles the rule when it fails an "and" rule or passes an "or" rule.
    private bool Decided(bool holds, out bool result)
    {
        result = holds;
        return AnyOf ? holds : !holds;
    }

    /// Marks a run as started unless the rule ran within its rate limit or is still running; true when it may run now.
    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRun) < RateLimitMs)
        {
            Volatile.Write(ref _running, 0);
            return false;
        }
        Interlocked.Exchange(ref _lastRun, now);
        return true;
    }

    public void Finished() => Volatile.Write(ref _running, 0);
}
