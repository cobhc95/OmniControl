using System.Text.Json.Serialization;

namespace OmniControl.App.Features.Input.Rules;

/// One custom gesture rule: "If &lt;trigger&gt; [and|or &lt;condition&gt;]..., then &lt;step&gt; [then &lt;step&gt;]...".
/// The trigger, each condition and each step are stored as the same "id:parameter|key=value;..." text that mouse buttons and
/// gestures use (<see cref="Features.Hotkeys.Actions.MouseAction"/>), so each is picked from a master list with the one picker.
public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool On { get; set; }

    /// The trigger (<see cref="RuleTriggers"/>); empty while none is chosen.
    public string Trigger { get; set; } = string.Empty;

    /// The conditions (<see cref="RuleConditions"/>); none = the trigger alone is enough.
    public List<string> Conditions { get; set; } = new();

    /// False: every condition must hold ("and"). True: any one is enough ("or"). One level only, no nested groups.
    public bool AnyOf { get; set; }

    /// What it does, in order (<see cref="RuleSteps"/>).
    public List<string> Steps { get; set; } = new();

    /// The mouse layer it works on; <see cref="AnyLayer"/> = every layer.
    public int Layer { get; set; } = AnyLayer;

    /// It runs at most once per this many milliseconds.
    public int RateLimitMs { get; set; } = (int)Timing.RuleRateLimit.TotalMilliseconds;

    public const int AnyLayer = 0;

    /// A rule can run once it has a trigger and something to do.
    [JsonIgnore]
    public bool IsComplete => Trigger.Length > 0 && Steps.Count > 0;

    /// The rate limit as used: the offered limit nearest to the stored one, so a hand-edited file cannot go outside them.
    public TimeSpan ClampedRateLimit() => Timing.RuleRateLimits.MinBy(t => Math.Abs(t.TotalMilliseconds - RateLimitMs));
}
