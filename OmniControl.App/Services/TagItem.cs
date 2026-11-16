namespace OmniControl.App.Services;

/// <summary>
/// The colour of a tag (Border.tag): no colour for a neutral fact, green for a plain advantage, yellow for the one thing to keep in
/// mind, red only for a real failure.
/// </summary>
public enum TagTone { Neutral, Good, Caution, Risk }

/// <summary>
/// One small fact shown as a tag (<c>Controls/TagView</c>): its words, its colour and the full sentence (the tooltip). The one record
/// every list of tags is made of (a rule's facts, a preset's facts, the cable status).
/// </summary>
public sealed record TagItem(string Text, TagTone Tone = TagTone.Neutral, string? Tip = null)
{
    public bool IsGood => Tone == TagTone.Good;
    public bool IsCaution => Tone == TagTone.Caution;
    public bool IsRisk => Tone == TagTone.Risk;
}
