using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Rules;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Gestures;

/// <summary>The facts of a rule as tags (its text, the full sentence as the tip, its tone).</summary>
public static class RuleTags
{
    /// <summary>Every fact worth showing about a rule, most important first.</summary>
    public static IReadOnlyList<TagItem> Of(Rule rule, RuleService rules)
    {
        var tags = new List<TagItem>();
        if (rules.ProblemOf(rule) is { } problem) tags.Add(new TagItem("Last run failed", TagTone.Risk, $"Last run: {problem}."));
        if (rule.Trigger.Length == 0) tags.Add(new TagItem("Needs a trigger", Tip: "Choose what starts the rule (the first block)."));
        else if (RuleTriggers.ProblemOf(rule.Trigger) is { } trigger) tags.Add(new TagItem("Shortcut can't be used", TagTone.Caution, trigger));
        if (rule.Steps.Count == 0) tags.Add(new TagItem("Needs an action", Tip: "Choose what the rule does (after \"then\")."));
        if (rules.ConflictOf(rule) is { } conflict) tags.Add(new TagItem(conflict.Chip, TagTone.Caution, conflict.Detail));
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

    [ObservableProperty] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

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
        Tags = RuleTags.Of(Rule, _rules);
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
