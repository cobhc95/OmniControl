namespace OmniControl.App.States;

/// <summary>
/// When a "When a state changes" trigger fires (pure, no UI): only on a real change from a known value, so start-up and a first reading
/// never fire it. On/off: turns on, turns off, changes. Level: goes above or below a value (crossing it), or becomes it (rounded to whole
/// numbers). Choice: becomes the chosen value.
/// </summary>
public static class StateTriggerMath
{
    public const string TurnsOn = "turns on", TurnsOff = "turns off", Changes = "changes";
    public const string Above = "goes above", Below = "goes below", Becomes = "becomes";

    /// <summary>The choices of the "When it" setting for each kind, in the order the picker offers them.</summary>
    public static IReadOnlyList<string> WhensOf(StateKind kind) => kind switch
    {
        StateKind.OnOff => new[] { TurnsOn, TurnsOff, Changes },
        StateKind.Level => new[] { Above, Below, Becomes },
        _ => new[] { Becomes },
    };

    /// <param name="when">One of <see cref="WhensOf"/> for the kind.</param>
    /// <param name="level">The value a level trigger compares with.</param>
    /// <param name="choice">The value a choice trigger waits for.</param>
    public static bool Fires(StateKind kind, StateValue? before, StateValue? after, string when, double level = 0, string choice = "")
    {
        if (before is null || after is null || after.SameValue(before)) return false;
        switch (kind)
        {
            case StateKind.OnOff:
                if (before.Number is not { } was || after.Number is not { } now) return false;
                return when switch
                {
                    TurnsOn => was == 0 && now != 0,
                    TurnsOff => was != 0 && now == 0,
                    Changes => (was != 0) != (now != 0),
                    _ => false,
                };
            case StateKind.Level:
                if (before.Number is not { } from || after.Number is not { } to) return false;
                return when switch
                {
                    Above => from <= level && to > level,
                    Below => from >= level && to < level,
                    Becomes => Math.Round(from) != Math.Round(level) && Math.Round(to) == Math.Round(level),
                    _ => false,
                };
            default:
                return after.Text == choice && before.Text != choice;
        }
    }
}
