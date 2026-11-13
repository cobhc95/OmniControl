using System.Text.Json.Serialization;
using OmniControl.App.Services;

namespace OmniControl.App.Rules;

/// <summary>
/// One custom gesture rule: "If &lt;trigger&gt; [and|or &lt;condition&gt;]..., then &lt;step&gt; [then &lt;step&gt;]...".
/// The trigger, each condition and each step are stored as the same "id:parameter|key=value;..." text that mouse buttons and
/// gestures use (<see cref="Mouse.MouseAction"/>), so each is picked from a master list with the one picker.
/// </summary>
public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool On { get; set; }

    /// <summary>The trigger (<see cref="RuleTriggers"/>); empty while none is chosen.</summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>The conditions (<see cref="RuleConditions"/>); none = the trigger alone is enough.</summary>
    public List<string> Conditions { get; set; } = new();

    /// <summary>False: every condition must hold ("and"). True: any one is enough ("or"). One level only, no nested groups.</summary>
    public bool AnyOf { get; set; }

    /// <summary>What it does, in order (<see cref="RuleSteps"/>).</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>The mouse layer it works on; <see cref="AnyLayer"/> = every layer.</summary>
    public int Layer { get; set; } = AnyLayer;

    /// <summary>It runs at most once per this many milliseconds.</summary>
    public int RateLimitMs { get; set; } = (int)Timing.RuleRateLimit.TotalMilliseconds;

    public const int AnyLayer = 0;

    /// <summary>A rule can run once it has a trigger and something to do.</summary>
    [JsonIgnore]
    public bool IsComplete => Trigger.Length > 0 && Steps.Count > 0;

    /// <summary>The rate limit as used: the offered limit nearest to the stored one, so a hand-edited file cannot go outside them.</summary>
    public TimeSpan ClampedRateLimit() => Timing.RuleRateLimits.MinBy(t => Math.Abs(t.TotalMilliseconds - RateLimitMs));
}
