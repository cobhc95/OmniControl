using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OmniControl.Core.Engine;

/// <summary>One element of an Android screen as <c>uiautomator dump</c> reports it.</summary>
public sealed record SimNode(string Text, string Description, string ResourceId, bool Clickable, int Left, int Top, int Right, int Bottom,
    string Package = "", bool Checkable = false, bool Checked = false, string ClassName = "", bool Selected = false, bool Enabled = true, bool Activated = false)
{
    public bool Holds(SimNode other) => Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
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
            (string?)e.Attribute("resource-id") ?? string.Empty, (string?)e.Attribute("clickable") == "true", Number(1), Number(2), Number(3), Number(4),
            (string?)e.Attribute("package") ?? string.Empty, (string?)e.Attribute("checkable") == "true", (string?)e.Attribute("checked") == "true", (string?)e.Attribute("class") ?? string.Empty,
            (string?)e.Attribute("selected") == "true", (string?)e.Attribute("enabled") != "false", (string?)e.Attribute("activated") == "true");
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
        if (step.Anchor.Length > 0)
        {
            var inCard = candidates.Where(n => CardTexts(n).Any(t => Same(t, step.Anchor))).ToList();
            if (inCard.Count > 0) candidates = inCard;
        }
        return candidates.OrderByDescending(n => n.Clickable).ThenBy(n => n.Area).FirstOrDefault();
    }

    private static readonly string[] ContainerClasses = { "ViewPager", "RecyclerView", "ScrollView", "ListView", "FrameLayout", "LinearLayout", "RelativeLayout", "ConstraintLayout", "ViewGroup", "GridView", "DrawerLayout" };

    private long ScreenArea => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Area);

    /// <summary>A page container (pager, list, layout) or an element covering most of the screen: never what a person means to tap.</summary>
    private bool IsContainer(SimNode n) => n.Area * 10 > ScreenArea * 6 || (!n.Clickable && ContainerClasses.Any(c => n.ClassName.EndsWith(c, StringComparison.Ordinal)));

    /// <summary>The card or row around an element: the smallest container (not most of the screen) with other words in it; the element itself when there is none.</summary>
    public SimNode CardOf(SimNode node)
    {
        var limit = ScreenArea * 6 / 10;
        return Nodes.Where(b => b.Holds(node) && b.Area > 0 && b.Area <= limit).OrderBy(b => b.Area)
            .FirstOrDefault(box => Nodes.Any(n => n.Text.Length > 0 && box.Holds(n) && !Same(n.Text, node.Text) && n != node)) ?? node;
    }

    /// <summary>The texts of the card or row around an element (<see cref="CardOf"/>), own words left out, top to bottom.</summary>
    public IReadOnlyList<string> CardTexts(SimNode node)
    {
        var box = CardOf(node);
        if (box == node) return Array.Empty<string>();
        return Nodes.Where(n => n.Text.Length > 0 && box.Holds(n) && !Same(n.Text, node.Text) && n != node).OrderBy(n => n.Top).ThenBy(n => n.Left).Select(n => n.Text.Trim()).Distinct().ToList();
    }

    /// <summary>The elements inside a box (the box itself included), top to bottom.</summary>
    public IReadOnlyList<SimNode> Inside(SimNode box) => Nodes.Where(box.Holds).OrderBy(n => n.Top).ThenBy(n => n.Left).ToList();

    /// <summary>The screen's size in the dump's pixels (the largest element's right and bottom edges).</summary>
    public (int Width, int Height) Size => Nodes.Count == 0 ? (0, 0) : (Nodes.Max(n => n.Right), Nodes.Max(n => n.Bottom));

    /// <summary>True when more than one element on the screen has the same text, description and id as this one.</summary>
    public bool IsAmbiguous(SimNode node) => Nodes.Count(n => n.ResourceId == node.ResourceId && Same(n.Text, node.Text) && Same(n.Description, node.Description)) > 1;

    /// <summary>What a person calls an element: its words, else "the card's name" + its id made readable ("Tapo L530's device switch").</summary>
    public string LabelFor(SimNode node)
    {
        if (node.Text.Length > 0) return node.Text;
        if (node.Description.Length > 0) return node.Description;
        var id = SimSteps.Humanise(node.ResourceId, capital: false);
        if (CardTexts(node).FirstOrDefault() is { } card) return id.Length > 0 ? $"{card}'s {id}" : card;
        return SimSteps.Humanise(node.ResourceId);
    }

    /// <summary>The app the screen belongs to: the package most elements report, not counting the system bars; empty when none.</summary>
    public string AppPackage => Nodes.Where(n => n.Package.Length > 0 && n.Package != SimSteps.SystemUiPackage)
        .GroupBy(n => n.Package).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;

    /// <summary>True when the screen shows this text (as an element's text or description).</summary>
    public bool Shows(string text) => text.Length > 0 && Nodes.Any(n => Same(n.Text, text) || Same(n.Description, text));

    /// <summary>The smallest element that holds the point, preferring one that can be tapped; null when none.</summary>
    public SimNode? At(int x, int y) => Nodes.Where(n => n.Contains(x, y) && n.Area > 0)
        .OrderByDescending(n => n.Clickable && (n.Text.Length > 0 || n.Description.Length > 0 || n.ResourceId.Length > 0)).ThenBy(n => n.Area).FirstOrDefault();

    /// <summary>The smallest element at the point that can be tapped and is no page container; null when there is none.</summary>
    public SimNode? TappableAt(int x, int y) => Nodes.Where(n => (n.Clickable || n.Checkable) && n.Contains(x, y) && n.Area > 0 && !IsContainer(n)).OrderBy(n => n.Area).FirstOrDefault();

    /// <summary>The step a tap at a point stands for: the tappable element there by what it says, else by its resource id (with the card it sits in when several look alike); null when nothing there can be tapped.</summary>
    public SimStep? TapAt(int x, int y)
    {
        if (TappableAt(x, y) is not { } node) return null;
        var step = SimSteps.Blank(SimStepKind.Tap);
        step.Text = node.Text;
        step.Description = node.Text.Length == 0 ? node.Description : string.Empty;
        step.ResourceId = node.ResourceId;
        if (!step.HasIdentity) return null;
        step.Label = LabelFor(node);
        if (IsAmbiguous(node)) step.Anchor = CardTexts(node).FirstOrDefault() ?? string.Empty;
        return step;
    }

    /// <summary>The words an element at a point shows (its text, else its description); empty when it shows none.</summary>
    public string WordsAt(int x, int y) => At(x, y) is { } node ? (node.Text.Length > 0 ? node.Text : node.Description) : string.Empty;

    internal static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
