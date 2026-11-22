using System.Text.Json.Serialization;

namespace OmniControl.App.Features.Automation;

/// Do: "When &lt;trigger&gt; [and &lt;condition&gt;] do &lt;action&gt;..."; Block: "While &lt;condition&gt;... don't allow &lt;action&gt;...".
public enum AutomationKind { Do, Block }

/// One automation rule. The trigger, each condition and each action are stored as the same "id:parameter|key=value;..." text that mouse
/// buttons and gesture rules use, so each is picked from a master list with the one picker. The hotkey that starts a rule is not
/// stored here: it is the shortcut of the rule's own action in the master list (<see cref="ActionId"/>).
public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AutomationKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool On { get; set; }

    /// What starts a Do rule (<see cref="AutomationTriggers"/>); empty while none is chosen, and always empty for a Block rule.
    public string Trigger { get; set; } = string.Empty;

    /// Further triggers of a Do rule: any one of them (or <see cref="Trigger"/>) starts it ("or"). Never a hotkey.
    public List<string> MoreTriggers { get; set; } = new();

    /// How trigger i joins the ones before it ("or" / "and", <see cref="AutomationJoins"/>); parallel to <see cref="AllTriggers"/> (entry 0
    /// is unused). Missing entries mean "or". "and": the rule starts only while that trigger's state holds too.
    public List<string> TriggerJoins { get; set; } = new();

    /// The apply action of the preset this rule switches to, made in the preset manager; empty for any other rule. When a state started it
    /// and its triggers stop holding, the preset in use before comes back.
    public string Preset { get; set; } = string.Empty;

    /// The conditions (<see cref="AutomationConditions"/>), read left to right with <see cref="ConditionJoins"/>.
    public List<string> Conditions { get; set; } = new();

    /// How condition i joins the ones before it (<see cref="AutomationJoins"/>); parallel to <see cref="Conditions"/> (entry 0 is
    /// unused). Missing entries (rules saved before the joins existed) mean "and".
    public List<string> ConditionJoins { get; set; } = new();

    /// A Do rule's steps (<see cref="AutomationSteps"/>), or the master actions a Block rule forbids ("omni:id").
    public List<string> Actions { get; set; } = new();

    /// Seconds to wait before action i ("then wait"); parallel to <see cref="Actions"/> (entry 0 is unused). 0 or missing = "then".
    public List<int> StepWaits { get; set; } = new();

    public DateTime? LastRan { get; set; }

    /// The id of this rule's own "Run: name" action in the master list (a Do rule only).
    [JsonIgnore]
    public string ActionId => ActionIdPrefix + Id;

    public const string ActionIdPrefix = "automation.rule.";

    /// Every trigger that starts the rule: the first, then the "or" ones.
    [JsonIgnore]
    public IEnumerable<string> AllTriggers => (Trigger.Length > 0 ? new[] { Trigger } : Array.Empty<string>()).Concat(MoreTriggers);

    public string JoinBefore(int condition) => condition > 0 && condition < ConditionJoins.Count ? ConditionJoins[condition] : AutomationJoins.And;

    public string TriggerJoinBefore(int trigger) => trigger > 0 && trigger < TriggerJoins.Count ? TriggerJoins[trigger] : AutomationJoins.Or;

    public int WaitBefore(int action) => action > 0 && action < StepWaits.Count ? StepWaits[action] : 0;

    public void SetJoinBefore(int condition, string join)
    {
        Pad(ConditionJoins, Conditions.Count, AutomationJoins.And);
        ConditionJoins[condition] = join;
    }

    public void SetWaitBefore(int action, int seconds)
    {
        Pad(StepWaits, Actions.Count, 0);
        StepWaits[action] = seconds;
    }

    public void AddCondition(string stored)
    {
        Pad(ConditionJoins, Conditions.Count, AutomationJoins.And);
        Conditions.Add(stored);
        ConditionJoins.Add(AutomationJoins.And);
    }

    public void RemoveCondition(int index)
    {
        Pad(ConditionJoins, Conditions.Count, AutomationJoins.And);
        Conditions.RemoveAt(index);
        ConditionJoins.RemoveAt(index);
    }

    public void SetTriggerJoin(int trigger, string join)
    {
        Pad(TriggerJoins, MoreTriggers.Count + 1, AutomationJoins.Or);
        TriggerJoins[trigger] = join;
    }

    public void AddTrigger(string stored)
    {
        if (Trigger.Length == 0)
        {
            Trigger = stored;
            return;
        }
        Pad(TriggerJoins, MoreTriggers.Count + 1, AutomationJoins.Or);
        MoreTriggers.Add(stored);
        TriggerJoins.Add(AutomationJoins.Or);
    }

    /// Trigger i goes with its join; when the first goes, the next takes its place (never a hotkey, so the rule's own action is not touched).
    public void RemoveTrigger(int index)
    {
        Pad(TriggerJoins, MoreTriggers.Count + 1, AutomationJoins.Or);
        TriggerJoins.RemoveAt(index);
        if (index > 0)
        {
            MoreTriggers.RemoveAt(index - 1);
            return;
        }
        Trigger = MoreTriggers.FirstOrDefault() ?? string.Empty;
        if (MoreTriggers.Count > 0) MoreTriggers.RemoveAt(0);
    }

    public void AddAction(string stored)
    {
        Pad(StepWaits, Actions.Count, 0);
        Actions.Add(stored);
        StepWaits.Add(0);
    }

    public void RemoveAction(int index)
    {
        Pad(StepWaits, Actions.Count, 0);
        Actions.RemoveAt(index);
        StepWaits.RemoveAt(index);
    }

    private static void Pad<T>(List<T> list, int count, T fill)
    {
        while (list.Count < count) list.Add(fill);
    }

    [JsonIgnore]
    public bool IsComplete => Kind == AutomationKind.Block
        ? Conditions.Count > 0 && Actions.Count > 0
        : Trigger.Length > 0 && Actions.Count > 0;
}
