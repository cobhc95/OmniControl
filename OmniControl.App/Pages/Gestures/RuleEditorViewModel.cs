using OmniControl.App.Dialogs;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.Rules;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Gestures;

/// <summary>
/// The rule editor: its name, the sentence of blocks (trigger, conditions joined by "and" or "or", steps), the Layer and Rate limit
/// drop-downs, Test and Delete. Every change is saved and applied at once. A command or program step is shown once in the confirm
/// window before it is saved (again whenever it changes); the approval is kept with a hash of the step.
/// </summary>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    /// <summary>The rule editor's keys (handled by RuleEditorWindow), listed in the Gestures page's Keyboard shortcuts.</summary>
    public const string KeyHelp =
        "Left/Right move through the sentence; Enter changes a block or adds one; Delete removes a block; Tab goes on to the settings and buttons; Esc closes.";

    private readonly RuleService _rules;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action _changed;
    private readonly Action _delete;
    private readonly Action _closed;
    private readonly ChoiceItem _layer;
    private readonly IReadOnlyList<string> _rateNames;
    private IReadOnlyList<string> _layerNames;

    public Rule Rule { get; }
    public string Title { get; }

    [ObservableProperty] private string _name;

    /// <summary>The sentence: <see cref="SentenceWord"/>, <see cref="SentenceBlock"/>, <see cref="SentenceJoin"/>, <see cref="SentenceAdd"/>.</summary>
    public ObservableCollection<object> Sentence { get; } = new();

    /// <summary>The Layer and Rate limit cards (shared card types, so the wheel, keys and look are the same as everywhere).</summary>
    public IReadOnlyList<object> Settings { get; }

    /// <summary>What is worth knowing about the rule now (the same facts as its line, with their full sentences).</summary>
    [ObservableProperty] private IReadOnlyList<TagItem> _notes = Array.Empty<TagItem>();

    /// <summary>The last run went wrong: "Copy details" is offered.</summary>
    [ObservableProperty] private bool _hasFailure;

    [ObservableProperty] private bool _canTest;

    /// <param name="isNew">The rule was just added (the window says "New rule").</param>
    /// <param name="ask">Shows the confirm window for a command; true when the user allowed it.</param>
    /// <param name="changed">The rule changed: its line on the page is refreshed.</param>
    /// <param name="delete">Deletes the rule (the window then closes).</param>
    /// <param name="closed">The window closed (a new rule left empty is dropped then).</param>
    public RuleEditorViewModel(Rule rule, bool isNew, RuleService rules, Func<DialogMessage, Task<bool>> ask, Action changed, Action delete, Action closed)
    {
        Rule = rule;
        Title = isNew ? "New rule" : "Edit rule";
        _rules = rules;
        _ask = ask;
        _changed = changed;
        _delete = delete;
        _closed = closed;
        _name = rule.Name;

        _layerNames = rules.LayerNames(rule);
        _rateNames = Services.Timing.RuleRateLimits.Select(t => $"Once per {t.TotalSeconds:0.##} s").ToList();
        _layer = new ChoiceItem("Layer", _layerNames, SelectedLayerName(), OnLayerPicked) { Half = true };
        var rate = new ChoiceItem("Rate limit", _rateNames, _rateNames[Services.Timing.RuleRateLimits.ToList().IndexOf(rule.ClampedRateLimit())],
            OnRatePicked) { Half = true };
        Settings = new object[] { _layer, rate };
        Rebuild();
        rules.ProblemsChanged += Rebuild;
    }

    /// <summary>The window closed: stops following the rule's runs.</summary>
    public void Closed()
    {
        _rules.ProblemsChanged -= Rebuild;
        _closed();
    }

    partial void OnNameChanged(string value)
    {
        Rule.Name = value.Trim();
        Save();
    }

    private void OnLayerPicked(string name)
    {
        var index = _layerNames.ToList().IndexOf(name);
        if (index < 0) return;
        Rule.Layer = index; // index 0 is "Any layer" (Rule.AnyLayer), then layer 1, 2...
        Save();
    }

    private string SelectedLayerName() => _layerNames[Math.Clamp(Rule.Layer, 0, _layerNames.Count - 1)];

    /// <summary>The trigger decides whose layers the Layer drop-down names (mouse or trackpad): shown again after each change, keeping the number.</summary>
    private void ShowLayerNames()
    {
        var names = _rules.LayerNames(Rule);
        if (names.SequenceEqual(_layerNames)) return;
        _layerNames = names;
        _layer.Load(_layerNames, SelectedLayerName());
    }

    private void OnRatePicked(string name)
    {
        var index = _rateNames.ToList().IndexOf(name);
        if (index < 0) return;
        Rule.RateLimitMs = (int)Services.Timing.RuleRateLimits[index].TotalMilliseconds;
        Save();
    }

    // ---- The sentence ----

    private void Rebuild()
    {
        Sentence.Clear();
        Sentence.Add(new SentenceWord("If"));
        Sentence.Add(Rule.Trigger.Length > 0
            ? new SentenceBlock(RuleTriggers.PhraseOf(Rule.Trigger), new RuleSlot(RuleSlotKind.Trigger, 0))
            : new SentenceAdd("+ trigger", new RuleSlot(RuleSlotKind.Trigger, 0)));
        for (var i = 0; i < Rule.Conditions.Count; i++)
        {
            if (i > 0) Sentence.Add(new SentenceJoin(RuleSentence.JoinWord(Rule.AnyOf)));
            Sentence.Add(new SentenceBlock(RuleConditions.PhraseOf(Rule.Conditions[i]), new RuleSlot(RuleSlotKind.Condition, i)));
        }
        Sentence.Add(new SentenceAdd("+ condition", new RuleSlot(RuleSlotKind.Condition, -1)));
        Sentence.Add(new SentenceWord(", then"));
        var steps = _rules.StepChoices();
        for (var i = 0; i < Rule.Steps.Count; i++)
        {
            if (i > 0) Sentence.Add(new SentenceWord("then"));
            Sentence.Add(new SentenceBlock(RuleSteps.PhraseOf(Rule.Steps[i], steps), new RuleSlot(RuleSlotKind.Step, i)));
        }
        Sentence.Add(new SentenceAdd(Rule.Steps.Count == 0 ? "+ action" : "+ then", new RuleSlot(RuleSlotKind.Step, -1)));

        ShowLayerNames();
        Notes = RuleTags.Of(Rule, _rules);
        HasFailure = _rules.ProblemOf(Rule) is not null;
        CanTest = Rule.Steps.Count > 0;
    }

    private string StoredAt(RuleSlot slot) => slot.IsNew ? string.Empty : slot.Kind switch
    {
        RuleSlotKind.Trigger => Rule.Trigger,
        RuleSlotKind.Condition => Rule.Conditions[slot.Index],
        _ => Rule.Steps[slot.Index],
    };

    /// <summary>The picker for a block or a "+": its window title and its model (the master list of that kind, on the block's current entry).</summary>
    public (string Title, PickerModel Model) PickerFor(RuleSlot slot)
    {
        var stored = StoredAt(slot);
        var (choices, noun) = slot.Kind switch
        {
            RuleSlotKind.Trigger => (_rules.TriggerChoices(), "trigger"),
            RuleSlotKind.Condition => (_rules.ConditionChoices(Rule, slot.IsNew ? null : stored), "condition"),
            _ => (_rules.StepChoices(), PickerModel.ActionNoun),
        };
        var (action, values) = MouseAction.Split(stored);
        var current = stored.Length == 0 ? null : choices.FirstOrDefault(c => c.Matches(action));
        var model = new PickerModel(PickChoice.Group(choices), current, values, currentParameter: current?.ParameterOf(action) ?? string.Empty)
        {
            Noun = noun,
        };
        return ($"Choose {(slot.IsNew ? "a" : "the")} {noun}", model);
    }

    private static string Compose(PickResult pick) =>
        MouseAction.Compose(pick.Choice.WithParameter(pick.Parameter?.Trim() ?? string.Empty), pick.Choice.Options, pick.Values);

    /// <summary>A setting of the block's current entry changed in the picker: applied at once, except a command, which waits for "Use this action" and the confirm window.</summary>
    public void TakeLive(RuleSlot slot, PickResult pick)
    {
        var stored = Compose(pick);
        if (slot.Kind == RuleSlotKind.Step && RuleSteps.NeedsApproval(stored)) return;
        Set(slot, stored);
    }

    /// <summary>The picker's answer for a block or a "+". An entry that takes text needs some; a new command is confirmed first.</summary>
    public async Task TakeAsync(RuleSlot slot, PickResult pick)
    {
        if (pick.Choice.ParamHint is not null && (pick.Parameter ?? string.Empty).Trim().Length == 0) return;
        var stored = Compose(pick);
        if (slot.Kind == RuleSlotKind.Step && RuleSteps.NeedsApproval(stored) && !_rules.IsApproved(stored))
        {
            var allowed = await _ask(new DialogMessage("Allow this command?",
                "The rule runs this command each time it triggers. Changing it later asks again.",
                "Allow", RuleSteps.Describe(stored), ShowDetails: true));
            if (!allowed) return;
            _rules.Approve(stored);
        }
        Set(slot, stored);
    }

    private void Set(RuleSlot slot, string stored)
    {
        switch (slot.Kind)
        {
            case RuleSlotKind.Trigger: Rule.Trigger = stored; break;
            case RuleSlotKind.Condition when slot.IsNew: Rule.Conditions.Add(stored); break;
            case RuleSlotKind.Condition: Rule.Conditions[slot.Index] = stored; break;
            case RuleSlotKind.Step when slot.IsNew: Rule.Steps.Add(stored); break;
            default: Rule.Steps[slot.Index] = stored; break;
        }
        Save();
    }

    /// <summary>The block's cross, or Delete on it.</summary>
    public void Remove(RuleSlot slot)
    {
        if (slot.IsNew) return;
        switch (slot.Kind)
        {
            case RuleSlotKind.Trigger: Rule.Trigger = string.Empty; break;
            case RuleSlotKind.Condition: Rule.Conditions.RemoveAt(slot.Index); break;
            default: Rule.Steps.RemoveAt(slot.Index); break;
        }
        Save();
    }

    /// <summary>"and" becomes "or" and back, for every condition of the rule.</summary>
    public void FlipJoin()
    {
        Rule.AnyOf = !Rule.AnyOf;
        Save();
    }

    public void Test() => _rules.Test(Rule);

    /// <summary>Asks first; true when the rule was deleted (the window closes).</summary>
    public async Task<bool> DeleteAsync()
    {
        if (!await _ask(Confirm.Delete("rule", Rule.Name))) return false;
        _delete();
        return true;
    }

    /// <summary>What "Copy details" puts on the clipboard: the last problem and where the log is.</summary>
    public string Details => $"{_rules.ProblemOf(Rule)}{Environment.NewLine}Log: {RuleService.LogPath}";

    /// <summary>A rule without what it needs cannot stay on; then everything is saved, applied and shown again.</summary>
    private void Save()
    {
        if (!Rule.IsComplete) Rule.On = false;
        _rules.Apply();
        Rebuild();
        _changed();
    }
}
