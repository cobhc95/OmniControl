using System.Collections.ObjectModel;
using OmniControl.App.Dialogs;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Automation;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Automation;

/// <summary>
/// One automation rule on the Rules tab: its switch, its name, the rule as a sentence of blocks ("When [at 07:30 on weekdays] [and on
/// battery] do [Lock] [+ action]", or "While [on battery] don't allow [Shut down]"), and its facts as tags. Every block opens the shared
/// picker over the master list of its kind; every change is saved and applied at once.
/// </summary>
public sealed partial class AutomationItem : ObservableObject, IEditorItem, IDisposable
{
    private readonly AutomationService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<AutomationItem> _deleted;
    private HotkeyAction? _action;
    private bool _loading;

    public AutomationRule Rule { get; }

    public bool IsBlock => Rule.Kind == AutomationKind.Block;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isOn;

    /// <summary>The sentence: <see cref="SentenceWord"/>, <see cref="SentenceBlock"/>, <see cref="SentenceAdd"/>.</summary>
    public ObservableCollection<object> Sentence { get; } = new();

    [ObservableProperty] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    /// <summary>The rule's own "Run: name" action while it is listening for a key (the page captures the keys).</summary>
    public HotkeyAction? OwnAction => _action;

    public string Label => Rule.Name;

    /// <summary>The sentence as plain text, for search.</summary>
    public string Line => string.Join(" ", Sentence.Select(s => s switch { SentenceWord w => w.Text, SentenceBlock b => b.Text, SentenceHotkey h => $"I press {h.Gesture}", SentenceLink l => l.Text, _ => string.Empty }));

    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    /// <param name="deleted">The rule was deleted (the page drops its line).</param>
    public AutomationItem(AutomationRule rule, AutomationService service, Func<DialogMessage, Task<bool>> ask,
        Action<AutomationItem> deleted)
    {
        Rule = rule;
        _service = service;
        _ask = ask;
        _deleted = deleted;
        _name = rule.Name;
        FollowAction();
        service.Changed += Refresh;
        Refresh();
    }

    public void Dispose()
    {
        _service.Changed -= Refresh;
        if (_action is not null) _action.PropertyChanged -= OnActionChanged;
    }

    // ---- The line ----

    private void Refresh()
    {
        _loading = true;
        IsOn = Rule.On;
        Name = Rule.Name;
        Rebuild();
        Tags = BuildTags();
        _loading = false;
        OnPropertyChanged(nameof(Line));
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_loading) return;
        Rule.On = value;
        Save();
    }

    partial void OnNameChanged(string value)
    {
        if (_loading || value.Trim() is not { Length: > 0 } name) return; // a blank name is not kept
        Rule.Name = name;
        _service.Renamed(Rule);
        FollowAction();
        Refresh();
    }

    private void Save()
    {
        _service.Edited(Rule);
        Refresh();
    }

    private void FollowAction()
    {
        if (_action is not null) _action.PropertyChanged -= OnActionChanged;
        _action = _service.ActionOf(Rule);
        if (_action is not null) _action.PropertyChanged += OnActionChanged;
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HotkeyAction.Gesture) or nameof(HotkeyAction.IsListening))
        {
            Refresh();
            NotifyCapture();
        }
        else if (e.PropertyName is nameof(HotkeyAction.Preview) or nameof(HotkeyAction.RejectedNote)) NotifyCapture();
    }

    private void NotifyCapture()
    {
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(CaptureKeys));
        OnPropertyChanged(nameof(CaptureNote));
    }

    private void Rebuild()
    {
        Sentence.Clear();
        var choices = _service.StepChoices(Rule);
        if (IsBlock)
        {
            Sentence.Add(new SentenceWord("While"));
            AddConditions();
            Sentence.Add(new SentenceWord("don't allow"));
            AddActions(choices, "+ action");
            return;
        }
        Sentence.Add(new SentenceWord("When"));
        AddTriggers();
        AddConditions();
        Sentence.Add(new SentenceWord("do"));
        AddActions(choices, Rule.Actions.Count == 0 ? "+ action" : "+ then");
    }

    private void AddTriggers()
    {
        if (Rule.Trigger.Length == 0)
        {
            Sentence.Add(new SentenceAdd("+ trigger", new RuleSlot(RuleSlotKind.Trigger, 0)));
            return;
        }
        var place = 0;
        foreach (var trigger in Rule.AllTriggers)
        {
            if (place > 0) Sentence.Add(new SentenceWord("or"));
            var slot = new RuleSlot(RuleSlotKind.Trigger, place++);
            Sentence.Add(AutomationTriggers.IsHotkey(trigger) ? HotkeyBlock(slot) : new SentenceBlock(_service.TriggerPhrase(trigger), slot));
        }
        Sentence.Add(new SentenceAdd("+ or", new RuleSlot(RuleSlotKind.Trigger, -1)));
    }

    private SentenceHotkey HotkeyBlock(RuleSlot slot)
    {
        var gesture = _action?.Gesture ?? string.Empty;
        var capturing = IsCapturing;
        return new SentenceHotkey(slot, gesture, Keycaps.From(capturing ? string.Empty : gesture, capturing ? HotkeyCapture.Ellipsis : "set keys"), capturing);
    }

    private void AddConditions()
    {
        for (var i = 0; i < Rule.Conditions.Count; i++)
        {
            if (i > 0) Sentence.Add(ConditionJoin(i));
            Sentence.Add(new SentenceBlock(AutomationConditions.PhraseOf(Rule.Conditions[i]), new RuleSlot(RuleSlotKind.Condition, i)));
        }
        Sentence.Add(new SentenceAdd(Rule.Conditions.Count == 0 && IsBlock ? "+ condition" : "+ and", new RuleSlot(RuleSlotKind.Condition, -1)));
    }

    private void AddActions(IReadOnlyList<PickChoice> choices, string addText)
    {
        for (var i = 0; i < Rule.Actions.Count; i++)
        {
            if (i > 0) Sentence.Add(IsBlock ? new SentenceWord("and") : ActionJoin(i));
            if (i > 0 && !IsBlock && Rule.WaitBefore(i) > 0) Sentence.Add(new SentenceWord(", then do"));
            Sentence.Add(new SentenceBlock(AutomationSteps.PhraseOf(Rule, Rule.Actions[i], choices), new RuleSlot(RuleSlotKind.Step, i)));
        }
        Sentence.Add(new SentenceAdd(addText, new RuleSlot(RuleSlotKind.Step, -1)));
    }

    private SentenceLink ConditionJoin(int condition)
    {
        var now = Rule.JoinBefore(condition);
        var options = AutomationJoins.All.Select(join => new SentenceLinkOption(AutomationJoins.WordOf(join), join == now, () =>
        {
            Rule.SetJoinBefore(condition, join);
            Save();
        })).ToList();
        return new SentenceLink(AutomationJoins.WordOf(now), options);
    }

    private SentenceLink ActionJoin(int action)
    {
        var seconds = Rule.WaitBefore(action);
        SentenceLink? link = null;
        var amount = seconds == 0 ? null : new SentenceLinkAmount("Wait for", 1, Timing.AutomationMaxWaitSeconds,
            Timing.AutomationDefaultWaitSeconds, seconds, " s", n =>
            {
                Rule.SetWaitBefore(action, n);
                _service.Tweaked();
                if (link is not null) link.Text = WaitWords(n);
            });
        var options = new[]
        {
            new SentenceLinkOption("then", seconds == 0, () => SetWait(action, 0)),
            new SentenceLinkOption("then wait", seconds > 0, () => SetWait(action, seconds > 0 ? seconds : Timing.AutomationDefaultWaitSeconds)),
        };
        return link = new SentenceLink(seconds == 0 ? "then" : WaitWords(seconds), options, amount);
    }

    private static string WaitWords(int seconds) => $"then wait {seconds} s";

    private void SetWait(int action, int seconds)
    {
        Rule.SetWaitBefore(action, seconds);
        Save();
    }

    private IReadOnlyList<TagItem> BuildTags()
    {
        var tags = new List<TagItem>();
        if (_service.ProblemOf(Rule) is { } problem) tags.Add(new TagItem("Last run failed", TagTone.Risk, $"Last run: {problem}."));
        if (_service.PausedWhyOf(Rule) is { } paused) tags.Add(new TagItem("Paused", TagTone.Caution, paused));
        if (!IsBlock && Rule.Trigger.Length == 0) tags.Add(new TagItem("Needs a trigger", Tip: "Choose what starts it (the first block)."));
        if (IsBlock && Rule.Conditions.Count == 0) tags.Add(new TagItem("Needs a condition", Tip: "Choose when it blocks (the first block)."));
        if (!IsBlock && Rule.AllTriggers.Any(AutomationTriggers.IsHotkey) && _action is { Gesture.Length: 0 })
            tags.Add(new TagItem("Needs a hotkey", Tip: "Click \"set keys\" and press the keys that start it."));
        if (Rule.Actions.Count == 0) tags.Add(new TagItem("Needs an action", Tip: IsBlock ? "Choose what it blocks." : "Choose what it does."));
        var (missingTrigger, missingAction) = _service.MissingOf(Rule);
        if (missingTrigger) tags.Add(new TagItem("Trigger missing", TagTone.Caution, "A state or action it starts from is gone (a device was removed?), so it never starts from that. Choose another."));
        if (missingAction) tags.Add(new TagItem("Action missing", TagTone.Caution, IsBlock ? "An action it blocks is gone." : "An action it does is gone, so that step is skipped. Choose another."));
        if (Rule.AllTriggers.Any(t => t.StartsWith(AutomationTriggers.Login, StringComparison.Ordinal)) && StartupRegistration.State != StartupState.On)
            tags.Add(new TagItem("Needs Start with Windows", TagTone.Caution, "\"At log in\" runs when Windows starts OmniControl. Turn on Start with Windows in App settings."));
        if (Rule.On && Rule.LastRan is { } ran) tags.Add(new TagItem($"Ran {ran:ddd HH:mm}", Tip: $"It last started on {ran:f}."));
        if (_service.NextRunOf(Rule) is { } next) tags.Add(new TagItem(next, Tip: "The next time it is due."));
        return tags;
    }

    // ---- The blocks ----

    private string StoredAt(RuleSlot slot) => slot.IsNew ? string.Empty : slot.Kind switch
    {
        RuleSlotKind.Trigger => slot.Index == 0 ? Rule.Trigger : Rule.MoreTriggers[slot.Index - 1],
        RuleSlotKind.Condition => Rule.Conditions[slot.Index],
        _ => Rule.Actions[slot.Index],
    };

    /// <summary>The picker for a block or a "+": its window title and its model (the master list of that kind, on the block's current entry).</summary>
    public (string Title, PickerModel Model) PickerFor(RuleSlot slot)
    {
        var stored = StoredAt(slot);
        var (choices, noun) = slot.Kind switch
        {
            RuleSlotKind.Trigger => (TriggerChoicesFor(slot), "trigger"),
            RuleSlotKind.Condition => (_service.ConditionChoices(Rule, slot.IsNew ? null : stored), "condition"),
            _ => (_service.StepChoices(Rule), PickerModel.ActionNoun),
        };
        var (action, values) = MouseAction.Split(stored);
        var current = stored.Length == 0 ? null : choices.FirstOrDefault(c => c.Matches(action));
        var model = new PickerModel(PickChoice.Group(choices), current, values, currentParameter: current?.ParameterOf(action) ?? string.Empty)
        {
            Noun = noun,
        };
        // The trigger list is read from the live master lists of states and actions: it follows them while the window is open.
        if (slot.Kind == RuleSlotKind.Trigger)
            model.Follow(new LiveList(() => TriggerChoicesFor(slot), h => _service.TriggersChanged += h, h => _service.TriggersChanged -= h));
        return ($"Choose {(slot.IsNew ? "a" : "the")} {noun}", model);
    }

    /// <summary>Only the first trigger can be the rule's hotkey (that is the rule's own action), so the "or" ones are offered without it.</summary>
    private IReadOnlyList<PickChoice> TriggerChoicesFor(RuleSlot slot) =>
        slot.Index == 0 ? _service.TriggerChoices() : _service.TriggerChoices().Where(c => c.Spec != AutomationTriggers.Hotkey).ToList();

    private static string Compose(PickResult pick) =>
        MouseAction.Compose(pick.Choice.WithParameter(pick.Parameter?.Trim() ?? string.Empty), pick.Choice.Options, pick.Values);

    /// <summary>A setting of the block's current entry changed in the picker: applied at once, except a program step, which waits for "Use this action" and the confirm window.</summary>
    public void TakeLive(RuleSlot slot, PickResult pick)
    {
        var stored = Compose(pick);
        if (slot.Kind == RuleSlotKind.Step && !IsBlock && AutomationSteps.NeedsApproval(stored)) return;
        Set(slot, stored);
    }

    /// <summary>The picker's answer for a block or a "+".</summary>
    public async Task TakeAsync(RuleSlot slot, PickResult pick)
    {
        if (pick.Choice.ParamHint is not null && (pick.Parameter ?? string.Empty).Trim().Length == 0 && !AutomationTriggers.AllowsEmptyText(pick.Choice.Spec)) return;
        var stored = Compose(pick);
        if (slot.Kind == RuleSlotKind.Step && !IsBlock && AutomationSteps.NeedsApproval(stored) && !_service.IsApproved(stored))
        {
            var allowed = await _ask(new DialogMessage("Allow this program?", "The rule opens this each time it runs. Changing it later asks again.",
                "Allow", RuleSteps.Describe(stored), ShowDetails: true));
            if (!allowed) return;
            _service.Approve(stored);
        }
        Set(slot, stored);
        if (slot.Kind == RuleSlotKind.Trigger && AutomationTriggers.IsHotkey(stored)) ListenForHotkey();
    }

    private void Set(RuleSlot slot, string stored)
    {
        switch (slot.Kind)
        {
            case RuleSlotKind.Trigger when slot.IsNew: Rule.MoreTriggers.Add(stored); break;
            case RuleSlotKind.Trigger when slot.Index == 0: Rule.Trigger = stored; break;
            case RuleSlotKind.Trigger: Rule.MoreTriggers[slot.Index - 1] = stored; break;
            case RuleSlotKind.Condition when slot.IsNew: Rule.AddCondition(stored); break;
            case RuleSlotKind.Condition: Rule.Conditions[slot.Index] = stored; break;
            case RuleSlotKind.Step when slot.IsNew: Rule.AddAction(stored); break;
            default: Rule.Actions[slot.Index] = stored; break;
        }
        Save();
    }

    /// <summary>The block's cross, or Delete on it.</summary>
    public void Remove(RuleSlot slot)
    {
        if (slot.IsNew) return;
        switch (slot.Kind)
        {
            case RuleSlotKind.Trigger when slot.Index > 0: Rule.MoreTriggers.RemoveAt(slot.Index - 1); break;
            case RuleSlotKind.Trigger: RemoveFirstTrigger(); break;
            case RuleSlotKind.Condition: Rule.RemoveCondition(slot.Index); break;
            default: Rule.RemoveAction(slot.Index); break;
        }
        Save();
    }

    /// <summary>The first trigger goes: the next "or" one takes its place (it is never a hotkey, so the rule's own action is not touched).</summary>
    private void RemoveFirstTrigger()
    {
        Rule.Trigger = Rule.MoreTriggers.FirstOrDefault() ?? string.Empty;
        if (Rule.MoreTriggers.Count > 0) Rule.MoreTriggers.RemoveAt(0);
    }

    // ---- The hotkey block ----

    /// <summary>The rule's shortcut is the shortcut of its own action in the master list; this waits for the keys (the page's key handler takes them).</summary>
    public void ListenForHotkey()
    {
        if (_action is not null) _action.IsListening = true;
    }

    public void StopListening()
    {
        if (_action is not null) _action.IsListening = false;
    }

    /// <summary>The capture pop-down's Clear: the rule has no shortcut and stops listening.</summary>
    public void ClearHotkey()
    {
        if (_action is null) return;
        _action.Gesture = string.Empty;
        _action.IsListening = false;
    }

    /// <summary>True while the rule's shortcut waits for keys (the capture pop-down is open).</summary>
    public bool IsCapturing => _action is { IsListening: true };

    /// <summary>The keys held right now, drawn in the capture pop-down; the dashed slot is the main key still to press.</summary>
    public IReadOnlyList<KeycapPart> CaptureKeys => Keycaps.From(_action?.Preview, HotkeyCapture.Ellipsis);

    /// <summary>The pop-down's one muted line.</summary>
    public string CaptureNote => _action is { RejectedNote: true } ? "Add Ctrl or Alt" : "Press the keys now";

    // ---- Deleting ----

    /// <summary>Asks first; the rule is removed when confirmed.</summary>
    public async Task DeleteAsync()
    {
        if (!await _ask(Confirm.Delete(IsBlock ? "block" : "automation", Rule.Name))) return;
        _service.Remove(Rule);
        _deleted(this);
    }
}
