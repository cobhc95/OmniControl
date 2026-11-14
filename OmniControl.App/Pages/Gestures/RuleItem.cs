using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Rules;

namespace OmniControl.App.Pages.Gestures;

/// <summary>A short fact on a rule's line (Border.tag): its text, the full sentence (tooltip, and the line in the editor), and its tone.</summary>
public sealed record RuleTag(string Text, string Detail, bool IsCaution = false, bool IsRisk = false)
{
    /// <summary>Every fact worth showing about a rule, most important first.</summary>
    public static IReadOnlyList<RuleTag> Of(Rule rule, RuleService rules)
    {
        var tags = new List<RuleTag>();
        if (rules.ProblemOf(rule) is { } problem) tags.Add(new RuleTag("Last run failed", $"Last run: {problem}.", IsRisk: true));
        if (rule.Trigger.Length == 0) tags.Add(new RuleTag("Needs a trigger", "Choose what starts the rule (the first block)."));
        else if (RuleTriggers.ProblemOf(rule.Trigger) is { } trigger) tags.Add(new RuleTag("Shortcut can't be used", trigger, IsCaution: true));
        if (rule.Steps.Count == 0) tags.Add(new RuleTag("Needs an action", "Choose what the rule does (after \"then\")."));
        if (rules.ConflictOf(rule) is { } conflict) tags.Add(new RuleTag(conflict.Chip, conflict.Detail, IsCaution: true));
        return tags;
    }
}

/// <summary>One rule's line on the Gestures page: its switch, its sentence, its facts, and an Edit pill.</summary>
public sealed partial class RuleItem : ObservableObject, IEditorItem
{
    private readonly RuleService _rules;
    private readonly Func<RuleItem, RuleEditorViewModel> _editorFor;
    private bool _loading;

    public Rule Rule { get; }

    [ObservableProperty] private bool _isOn;

    /// <summary>"Name: If ... then ...", or the sentence alone for a rule without a name.</summary>
    [ObservableProperty] private string _line = string.Empty;

    [ObservableProperty] private IReadOnlyList<RuleTag> _tags = Array.Empty<RuleTag>();

    /// <summary>What search shows and matches: the name, or the sentence.</summary>
    public string Label => Rule.Name.Trim() is { Length: > 0 } name ? name : Line;

    /// <param name="editorFor">Makes the editor for this rule (the page owns what happens on delete and close).</param>
    public RuleItem(Rule rule, RuleService rules, Func<RuleItem, RuleEditorViewModel> editorFor)
    {
        Rule = rule;
        _rules = rules;
        _editorFor = editorFor;
        Refresh();
    }

    /// <summary>A fresh editor on this rule (the Edit pill).</summary>
    public RuleEditorViewModel Editor() => _editorFor(this);

    /// <summary>Shows the rule as stored now (after an edit, a run, or a change elsewhere that makes or ends a conflict).</summary>
    public void Refresh()
    {
        _loading = true;
        var sentence = _rules.SentenceOf(Rule);
        Line = Rule.Name.Trim() is { Length: > 0 } name ? $"{name}: {sentence}" : sentence;
        Tags = RuleTag.Of(Rule, _rules);
        IsOn = Rule.On;
        _loading = false;
        OnPropertyChanged(nameof(Label));
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_loading) return;
        Rule.On = value;
        _rules.Apply();
    }
}
