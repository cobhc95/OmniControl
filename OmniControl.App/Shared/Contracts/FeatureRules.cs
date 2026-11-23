namespace OmniControl.App.Shared.Contracts;

/// What starts a feature's rule, in the feature's words; the rules turn it into a trigger of their master list.
public abstract record FeatureTrigger
{
    /// From <see cref="Start"/> to <see cref="End"/> (minutes of the day; may cross midnight): the rule starts as it begins and ends with it.
    public sealed record Between(int Start, int End) : FeatureTrigger;

    /// While a state of the master list is on (it starts as it turns on and ends as it turns off).
    public sealed record While(string StateId) : FeatureTrigger;

    /// When a momentary state of the master list fires (e.g. a mouse button press: one-shot, never holds).
    public sealed record Press(string StateId) : FeatureTrigger;

    /// When OmniControl starts.
    public sealed record Starts : FeatureTrigger;

    /// Every so many minutes (the "After a timer" trigger, repeating).
    public sealed record Every(int Minutes) : FeatureTrigger;
}

/// A feature's rule as its page shows it: on or off, the triggers the feature knows (in the rule's order), and what was added to it in
/// Automation ("1 extra condition"; empty: nothing), and whether Automation's page is there to edit it in (false while "Hotkeys and automation"
/// is switched off: the rule still runs).
public sealed record FeatureRuleView(bool On, IReadOnlyList<FeatureTrigger> Triggers, string Extras, bool Editable = true,
    IReadOnlyList<string>? Actions = null, bool InEffect = false)
{
    /// The rule's time window, or null.
    public FeatureTrigger.Between? Window => Triggers.OfType<FeatureTrigger.Between>().FirstOrDefault();

    /// The rule's repeat in minutes, or null.
    public int? Every => Triggers.OfType<FeatureTrigger.Every>().FirstOrDefault()?.Minutes;

    public bool AtStart => Triggers.OfType<FeatureTrigger.Starts>().Any();
}

/// The automation rules a feature acts through. A rule belongs to a feature because it holds that feature's own rule action
/// (<c>HotkeyAction.FeatureRule</c>); nothing else marks it, so a rule that loses the action leaves the feature. Every run passes the one run path.
public interface IFeatureRules
{
    /// A rule changed (UI thread).
    event Action? Changed;

    /// A rule owned by a feature started holding (its triggers and conditions began to hold).
    event Action<string>? Started;

    /// A rule holding this action (its id) stopped holding: its triggers or conditions ended, it was switched off, deleted or paused.
    event Action<string>? Ended;

    /// The first rule holding this action, or null when none does.
    FeatureRuleView? Find(string actionId);

    /// Makes a rule with this action and these triggers unless one holds the action already.
    void Ensure(string actionId, string name, bool on, params FeatureTrigger[] triggers);

    /// Makes a rule owned by <paramref name="featureId"/> on <paramref name="page"/> with <paramref name="actions"/> and
    /// <paramref name="triggers"/>, or updates its actions when the rule already exists.
    void EnsureActions(string featureId, string name, bool on, string page, IReadOnlyList<string> actions, params FeatureTrigger[] triggers);

    /// Removes the rule holding this action, if one exists.
    void Remove(string actionId);

    void SetOn(string actionId, bool on);

    /// Puts this trigger in place of the rule's trigger of the same kind (or adds it).
    void SetTrigger(string actionId, FeatureTrigger trigger);

    /// Takes the rule's triggers of this kind out (a rule keeps at least one trigger).
    void RemoveTrigger<T>(string actionId) where T : FeatureTrigger;

    /// Opens Automation where the rule is.
    void Edit(string actionId);
}

/// Where the rules plug in their <see cref="IFeatureRules"/> (made by the shell, as the action gate's slot is): features read it; the rules run
/// whether "Hotkeys and automation" is on or off, so it is empty only while they restart or OmniControl shuts down.
public sealed class FeatureRulesSlot
{
    public IFeatureRules? Current { get; private set; }

    public event Action? Changed;
    public event Action<string>? Started;
    public event Action<string>? Ended;

    public void Attach(IFeatureRules rules)
    {
        Current = rules;
        rules.Changed += OnChanged;
        rules.Started += OnStarted;
        rules.Ended += OnEnded;
        OnChanged();
    }

    public void Detach(IFeatureRules rules)
    {
        if (!ReferenceEquals(Current, rules)) return;
        rules.Changed -= OnChanged;
        rules.Started -= OnStarted;
        rules.Ended -= OnEnded;
        Current = null;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke();

    private void OnStarted(string actionId) => Started?.Invoke(actionId);

    private void OnEnded(string actionId) => Ended?.Invoke(actionId);
}
