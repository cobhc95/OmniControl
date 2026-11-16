namespace OmniControl.Core.Engine;

/// <summary>
/// How a taught device's switch is found again: by a text of its card that showed in every look (the device's name, the <see cref="Anchor"/>)
/// and where the switch sat from it, plus the switch's id and class; its words only when they did not change with the state. So the card is
/// found when it moved or the list scrolled, and an identical switch in another device's card is never taken for it.
/// </summary>
public sealed class StateTarget
{
    public string Anchor { get; set; } = string.Empty;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Where the switch was on the screen, used only when its card has no text to find it by.</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>The target as every look agrees on it; the anchor is the card text whose offset moved least between the looks.</summary>
    public static StateTarget From(IReadOnlyList<StateSample> samples)
    {
        string Same(Func<StateSample, string> pick) => samples.Select(pick).Distinct().Count() == 1 ? pick(samples[0]) : string.Empty;
        var target = new StateTarget
        {
            ResourceId = Same(s => s.TargetId), ClassName = Same(s => s.TargetClass), Text = Same(s => s.TargetText), Description = Same(s => s.TargetDescription),
            X = (int)samples.Average(s => s.TargetX), Y = (int)samples.Average(s => s.TargetY),
        };
        var anchor = samples[0].Anchors.Keys.Where(text => samples.All(s => s.Anchors.ContainsKey(text)))
            .Select(text => (Text: text, Offsets: samples.Select(s => s.Anchors[text]).ToList()))
            .OrderBy(a => a.Offsets.Max(o => Math.Abs(o[0] - a.Offsets[0][0]) + Math.Abs(o[1] - a.Offsets[0][1])))
            .FirstOrDefault();
        if (anchor.Text is not null)
            (target.Anchor, target.OffsetX, target.OffsetY) = (anchor.Text, (int)anchor.Offsets.Average(o => o[0]), (int)anchor.Offsets.Average(o => o[1]));
        return target;
    }

    /// <summary>The switch on this screen; null when its card is not on it (scroll and look again).</summary>
    public SimNode? Locate(SimScreen screen)
    {
        var candidates = screen.Nodes.Where(n => n.Area > 0
            && (ResourceId.Length == 0 || n.ResourceId == ResourceId) && (ClassName.Length == 0 || n.ClassName == ClassName)
            && (Text.Length == 0 || SimScreen.Same(n.Text, Text)) && (Description.Length == 0 || SimScreen.Same(n.Description, Description))).ToList();
        if (candidates.Count == 0) return null;
        if (Anchor.Length == 0)
            return candidates.OrderBy(c => Math.Abs(c.CenterX - X) + Math.Abs(c.CenterY - Y)).First();
        var slack = Math.Max(1, screen.Size.Width) * CoreTiming.StateAnchorSlack;
        return (from anchor in screen.Nodes.Where(n => SimScreen.Same(n.Text, Anchor))
                from c in candidates
                let miss = Math.Abs(c.CenterX - anchor.CenterX - OffsetX) + Math.Abs(c.CenterY - anchor.CenterY - OffsetY)
                where miss <= slack
                orderby miss
                select c).FirstOrDefault();
    }
}
