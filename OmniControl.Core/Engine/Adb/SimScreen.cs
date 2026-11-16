using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OmniControl.Core.Engine;

/// <summary>One element of an Android screen as <c>uiautomator dump</c> reports it.</summary>
public sealed record SimNode(string Text, string Description, string ResourceId, bool Clickable, int Left, int Top, int Right, int Bottom)
{
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;
    public long Area => (long)Math.Max(0, Right - Left) * Math.Max(0, Bottom - Top);
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>One screen read from a uiautomator dump: what is on it, how to find an element by its text, description or resource id, and which element is at a point.</summary>
public sealed class SimScreen
{
    private static readonly Regex BoundsPattern = new(@"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]", RegexOptions.Compiled);

    private SimScreen(IReadOnlyList<SimNode> nodes) => Nodes = nodes;

    public IReadOnlyList<SimNode> Nodes { get; }

    /// <summary>Reads a dump (adb may print a line before or after the XML); null when it holds no screen.</summary>
    public static SimScreen? Parse(string? dump)
    {
        if (dump is null) return null;
        var start = dump.IndexOf("<hierarchy", StringComparison.Ordinal);
        var end = dump.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (start < 0 || end < start) return null;
        try
        {
            var root = XDocument.Parse(dump[start..(end + "</hierarchy>".Length)]).Root;
            return new SimScreen(root!.Descendants("node").Select(NodeOf).Where(n => n is not null).Select(n => n!).ToList());
        }
        catch (XmlException)
        {
            return null; // a dump cut short by a timeout is no screen
        }
    }

    private static SimNode? NodeOf(XElement e)
    {
        var match = BoundsPattern.Match((string?)e.Attribute("bounds") ?? string.Empty);
        if (!match.Success) return null;
        int Number(int group) => int.Parse(match.Groups[group].Value);
        return new SimNode((string?)e.Attribute("text") ?? string.Empty, (string?)e.Attribute("content-desc") ?? string.Empty,
            (string?)e.Attribute("resource-id") ?? string.Empty, (string?)e.Attribute("clickable") == "true", Number(1), Number(2), Number(3), Number(4));
    }

    /// <summary>The element a step names: its text and description must match (the whole text, ignoring case) and its resource id narrows the choice. Null when there is none.</summary>
    public SimNode? Find(SimStep step)
    {
        IEnumerable<SimNode> found = Nodes;
        if (step.Text.Length > 0) found = found.Where(n => Same(n.Text, step.Text));
        if (step.Description.Length > 0) found = found.Where(n => Same(n.Description, step.Description));
        var candidates = found.ToList();
        if (step.ResourceId.Length > 0)
        {
            var byId = candidates.Where(n => n.ResourceId == step.ResourceId).ToList();
            if (byId.Count > 0 || (step.Text.Length == 0 && step.Description.Length == 0)) candidates = byId;
        }
        return candidates.OrderByDescending(n => n.Clickable).ThenBy(n => n.Area).FirstOrDefault();
    }

    /// <summary>True when the screen shows this text (as an element's text or description).</summary>
    public bool Shows(string text) => text.Length > 0 && Nodes.Any(n => Same(n.Text, text) || Same(n.Description, text));

    /// <summary>The smallest element that holds the point, preferring one that can be tapped; null when none.</summary>
    public SimNode? At(int x, int y) => Nodes.Where(n => n.Contains(x, y) && n.Area > 0)
        .OrderByDescending(n => n.Clickable && (n.Text.Length > 0 || n.Description.Length > 0 || n.ResourceId.Length > 0)).ThenBy(n => n.Area).FirstOrDefault();

    /// <summary>The step a tap at a point stands for: the element there by what it says, else by its resource id, else the point itself.</summary>
    public SimStep TapAt(int x, int y)
    {
        var step = SimSteps.Blank(SimStepKind.Tap);
        if (At(x, y) is { } node)
        {
            step.Text = node.Text;
            step.Description = node.Text.Length == 0 ? node.Description : string.Empty;
            step.ResourceId = node.ResourceId;
            if (!step.HasIdentity) { step.X = x; step.Y = y; }
            return step;
        }
        step.X = x;
        step.Y = y;
        return step;
    }

    /// <summary>The words an element at a point shows (its text, else its description); empty when it shows none.</summary>
    public string WordsAt(int x, int y) => At(x, y) is { } node ? (node.Text.Length > 0 ? node.Text : node.Description) : string.Empty;

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
