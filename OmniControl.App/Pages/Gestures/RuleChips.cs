namespace OmniControl.App.Pages.Gestures;

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

/// <summary>The word between conditions; clicking it flips the rule between "and" and "or".</summary>
public sealed record SentenceJoin(string Text);

/// <summary>The dashed "+" that adds a part (or fills an empty trigger).</summary>
public sealed record SentenceAdd(string Text, RuleSlot Slot);
