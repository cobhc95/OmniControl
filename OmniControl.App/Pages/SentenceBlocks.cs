using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages;

/// <summary>Which part of a rule a block stands for.</summary>
public enum RuleSlotKind { Trigger, Condition, Step }

/// <summary>A place in a rule: the trigger, one condition or one step by index; <see cref="Index"/> -1 = a new one at the end.</summary>
public sealed record RuleSlot(RuleSlotKind Kind, int Index)
{
    public bool IsNew => Index < 0;
}

// The parts of the editor's sentence ("If [right click] [over the taskbar] [and] [Ctrl held] [+ condition], then [...] [+ then]"),
// each drawn by its own template in RuleEditorWindow with the master block styles (Styles/Blocks.axaml).

/// <summary>A plain word of the sentence ("If", "then").</summary>
public sealed record SentenceWord(string Text);

/// <summary>A chosen part: clicking it changes it, its cross (or Delete) removes it.</summary>
public sealed record SentenceBlock(string Text, RuleSlot Slot);

/// <summary>A hotkey trigger ("I press" + keycaps, or the dashed "set keys" slot); clicking it opens the capture pop-down while <see cref="Capturing"/>.</summary>
public sealed record SentenceHotkey(RuleSlot Slot, string Gesture, IReadOnlyList<KeycapPart> Keys, bool Capturing);

/// <summary>The word between conditions; clicking it flips the rule between "and" and "or".</summary>
public sealed record SentenceJoin(string Text);

/// <summary>The dashed "+" that adds a part (or fills an empty trigger).</summary>
public sealed record SentenceAdd(string Text, RuleSlot Slot);

/// <summary>One thing a <see cref="SentenceLink"/> can say ("and not", "then wait"); <see cref="IsOn"/> marks the one it says now.</summary>
public sealed record SentenceLinkOption(string Text, bool IsOn, Action Choose);

/// <summary>
/// The number that belongs to a <see cref="SentenceLink"/> option (the seconds of "then wait"), edited in the link's pop-down with a
/// slider and typing. It reports every change through <c>changed</c> and leaves the sentence as it is, so the pop-down stays open.
/// </summary>
public sealed partial class SentenceLinkAmount : ObservableObject
{
    private readonly Action<int> _changed;

    public SentenceLinkAmount(string label, int minimum, int maximum, int standard, int value, string unit, Action<int> changed)
    {
        Label = label;
        Minimum = minimum;
        Maximum = maximum;
        Standard = standard;
        Unit = unit;
        _value = value;
        _changed = changed;
    }

    public string Label { get; }
    public double Minimum { get; }
    public double Maximum { get; }

    /// <summary>What a double-click on the slider goes back to.</summary>
    public double Standard { get; }

    public string Unit { get; }

    public string Text => $"{Value:0}{Unit}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Text))] private double _value;

    partial void OnValueChanged(double value) => _changed((int)Math.Round(value));
}

/// <summary>
/// The word between two parts of a sentence that can be picked from a short list (conditions: "and", "or", "and not"; actions:
/// "then", "then wait"). Drawn as a Button.blockjoin that opens the shared menu pop-down; <see cref="Text"/> follows the number.
/// </summary>
public sealed partial class SentenceLink : ObservableObject
{
    public SentenceLink(string text, IReadOnlyList<SentenceLinkOption> options, SentenceLinkAmount? amount = null)
    {
        _text = text;
        Options = options;
        Amount = amount;
    }

    [ObservableProperty] private string _text;

    public IReadOnlyList<SentenceLinkOption> Options { get; }
    public SentenceLinkAmount? Amount { get; }
    public bool HasAmount => Amount is not null;
}
