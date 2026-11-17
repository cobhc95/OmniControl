using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine;

/// <summary>
/// One look at a device's card in its app: every fact and measure a state reading can compare. Facts are words that are there or not, or an
/// element's attribute ("attr:Switch#app:id/power@0.checked" = "true", "text:on", "word:off", "has:percent", "view:ImageView#app:id/glow");
/// measures are numbers ("colour:target", "colour:card", "colourful:target", "shape:target"). Saved with the device when it is taught.
/// </summary>
public sealed class StateSample
{
    public const string Present = "1";

    public Dictionary<string, string> Facts { get; set; } = new();
    public Dictionary<string, double[]> Measures { get; set; } = new();

    /// <summary>No usable screenshot went with this look (none, blank or the wrong size), so only its dump facts count in a reading.</summary>
    public bool PixelsMissing { get; set; }

    /// <summary>The element that was looked at (the device's switch): its words, id and class, and its centre on the screen.</summary>
    public string TargetText { get; set; } = string.Empty;
    public string TargetDescription { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetClass { get; set; } = string.Empty;
    public int TargetX { get; set; }
    public int TargetY { get; set; }

    /// <summary>Where the target sat from each text of its card (the target's centre minus the text's centre), so the card can be found again by its name.</summary>
    public Dictionary<string, int[]> Anchors { get; set; } = new();

    private static readonly Regex WordPattern = new(@"\p{L}{2,}", RegexOptions.Compiled);
    private static readonly Regex PercentPattern = new(@"\d+\s*%", RegexOptions.Compiled);

    /// <summary>Takes the facts of the target's card from the dump and, when there is a screenshot, the colour and shape measures from its pixels.</summary>
    public static StateSample Take(SimScreen screen, SimNode target, RgbaImage? image)
    {
        var sample = new StateSample
        {
            TargetText = target.Text.Trim(), TargetDescription = target.Description.Trim(), TargetId = target.ResourceId, TargetClass = target.ClassName,
            TargetX = target.CenterX, TargetY = target.CenterY,
        };
        var card = screen.CardOf(target);
        var ordinals = new Dictionary<string, int>();
        foreach (var node in screen.Inside(card))
        {
            var kind = KindOf(node);
            var ordinal = ordinals[kind] = ordinals.GetValueOrDefault(kind, -1) + 1;
            var at = $"attr:{kind}@{ordinal}";
            sample.Facts[$"{at}.checked"] = Bool(node.Checked);
            sample.Facts[$"{at}.selected"] = Bool(node.Selected);
            sample.Facts[$"{at}.enabled"] = Bool(node.Enabled);
            sample.Facts[$"{at}.activated"] = Bool(node.Activated);
            sample.Facts[$"view:{kind}"] = Present;
            foreach (var words in new[] { node.Text.Trim(), node.Description.Trim() }.Where(w => w.Length > 0))
            {
                sample.Facts[$"text:{words.ToLowerInvariant()}"] = Present;
                foreach (Match word in WordPattern.Matches(words)) sample.Facts[$"word:{word.Value.ToLowerInvariant()}"] = Present;
                if (PercentPattern.IsMatch(words)) sample.Facts["has:percent"] = Present;
            }
            if (node != target && node.Text.Trim().Length > 0)
                sample.Anchors.TryAdd(node.Text.Trim(), new[] { target.CenterX - node.CenterX, target.CenterY - node.CenterY });
        }
        sample.PixelsMissing = image is null;
        if (image is not null) Measure(sample, screen, target, card, image);
        return sample;
    }

    private static void Measure(StateSample sample, SimScreen screen, SimNode target, SimNode card, RgbaImage image)
    {
        var (width, height) = screen.Size;
        double sx = width > 0 ? (double)image.Width / width : 1, sy = height > 0 ? (double)image.Height / height : 1;
        PixelBox Box(SimNode n) => new((int)(n.Left * sx), (int)(n.Top * sy), (int)(n.Right * sx), (int)(n.Bottom * sy));
        void Put(string key, double[]? value) { if (value is not null) sample.Measures[key] = value; }
        Put("colour:target", ImageStats.DominantColour(image, Box(target)));
        Put("colourful:target", ImageStats.Colourful(image, Box(target)));
        Put("colour:card", ImageStats.MeanColour(image, Box(card)));
        Put("shape:target", ImageStats.ShapeHash(image, Box(target)));
    }

    /// <summary>An element's kind: its short class name and resource id (<c>Switch#app:id/power</c>).</summary>
    private static string KindOf(SimNode node) => $"{node.ClassName[(node.ClassName.LastIndexOf('.') + 1)..]}#{node.ResourceId}";

    private static string Bool(bool value) => value ? "true" : "false";
}
