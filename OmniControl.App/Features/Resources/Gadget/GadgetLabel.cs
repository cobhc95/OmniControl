using System.Text;

namespace OmniControl.App.Features.Resources.Gadget;

/// One piece of a gadget label: words, or an icon (a library id or a user icon) with its own colour (a <see cref="Palette.DrawingNamed"/> name) or none.
public sealed record LabelPart(string? Text, string? Icon, string? Colour = null)
{
    /// The icon's own colour, or null for the cell's colour.
    public Color? Ink => GadgetLabel.InkOf(Colour);
}

/// The label styles one click gives every reading (Labels: Text / Icons / Both) or one reading (the label editor's quick chips).
public enum LabelStyle { Text, Icons, Both, None }

/// THE gadget label: words with icons written as {id} or, coloured, {id:colour} ("{cpu}{temperature:orange}:"), so a saved label stays a plain string. Pure.
public static class GadgetLabel
{
    private const int MaxIdLength = 64;

    /// The fixed meaning of colour in the built-in labels: upload, download, CPU, GPU, a warm and a hot temperature.
    public const string UploadColour = "green", DownloadColour = "blue", CpuColour = "yellow", GpuColour = "purple", WarmColour = "orange", HotColour = "red";

    /// The colours the label editor offers an icon: every drawing colour but black, which the dark taskbar would hide.
    public static readonly IReadOnlyList<string> IconColours = Palette.DrawingNamed.Select(d => d.Name).Where(n => n != "black").ToArray();

    public static string Token(string iconId, string? colour = null) => "{" + iconId + (colour is null ? string.Empty : ":" + colour) + "}";

    /// A drawing colour's value by its name, or null for none (the text's colour).
    public static Color? InkOf(string? colour) => colour is null ? null : Palette.DrawingNamed.First(d => d.Name == colour).Colour;

    /// The label's words and icons in order; a brace that does not hold an icon id stays words.
    public static IReadOnlyList<LabelPart> Parts(string label) => Spans(label).Select(s => s.Part).ToList();

    /// Each part with where its text starts in the label and how long it is.
    private static IEnumerable<(LabelPart Part, int Start, int Length)> Spans(string label)
    {
        var words = new StringBuilder();
        var from = 0;
        for (var i = 0; i < label.Length; i++)
        {
            var end = label[i] == '{' ? label.IndexOf('}', i + 1) : -1;
            if (end > i + 1 && end - i <= MaxIdLength && IsId(label.AsSpan(i + 1, end - i - 1)))
            {
                if (words.Length > 0) yield return (new LabelPart(words.ToString(), null), from, i - from);
                words.Clear();
                yield return (Icon(label[(i + 1)..end]), i, end - i + 1);
                i = end;
                from = end + 1;
            }
            else words.Append(label[i]);
        }
        if (words.Length > 0) yield return (new LabelPart(words.ToString(), null), from, label.Length - from);
    }

    /// The icon token the caret sits inside (between its braces): where it starts and how long it is, or null.
    public static (int Start, int Length)? TokenAt(string label, int caret) =>
        Spans(label).Where(s => s.Part.Icon is not null && caret > s.Start && caret < s.Start + s.Length).Select(s => ((int, int)?)(s.Start, s.Length)).FirstOrDefault();

    /// The label with the icon whose token starts at <paramref name="start"/> (null: every icon) in this colour (null: the text's colour).
    public static string Recolour(string label, int? start, string? colour) =>
        Write(Spans(label).Select(s => s.Part.Icon is not null && (start is null || s.Start == start) ? s.Part with { Colour = colour } : s.Part));

    /// The colour of the icon whose token starts at <paramref name="start"/> (null: the one colour all the label's icons share), or null when none or mixed.
    public static string? ColourOf(string label, int? start)
    {
        var colours = Spans(label).Where(s => s.Part.Icon is not null && (start is null || s.Start == start)).Select(s => s.Part.Colour).Distinct().Take(2).ToList();
        return colours.Count == 1 ? colours[0] : null;
    }

    /// An icon token's id and colour: a last ":name" that is a drawing colour's name is its colour (a user icon's id, "user:…", has none of those names).
    private static LabelPart Icon(string token)
    {
        var at = token.LastIndexOf(':');
        var colour = at > 0 ? Palette.DrawingNamed.Select(d => d.Name).FirstOrDefault(n => string.Equals(n, token[(at + 1)..], StringComparison.OrdinalIgnoreCase)) : null;
        return colour is null ? new LabelPart(null, token) : new LabelPart(null, token[..at], colour);
    }

    private static bool IsId(ReadOnlySpan<char> id)
    {
        foreach (var c in id) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or ':' or '_')) return false;
        return true;
    }

    private static string Write(IEnumerable<LabelPart> parts) => string.Concat(parts.Select(p => p.Icon is { } icon ? Token(icon, p.Colour) : p.Text));

    /// The label without its icons' colours.
    private static string Plain(string label) => Write(Parts(label).Select(p => p with { Colour = null }));

    /// The label without this icon (coloured or not).
    public static string Without(string label, string iconId) => Write(Parts(label).Where(p => p.Icon != iconId));

    /// The reading's label in this style: its words, its icons, both, or nothing.
    public static string Of(LabelStyle style, Reading reading) => style switch
    {
        LabelStyle.Text => reading.GadgetLabel,
        LabelStyle.Icons => reading.GadgetIcons,
        LabelStyle.Both => reading.GadgetIcons + reading.GadgetLabel,
        _ => string.Empty,
    };

    /// The style <paramref name="label"/> is for the reading whatever its icons' colours (a label saved before icons had colours), or null when the user wrote their own.
    public static LabelStyle? StyleOf(string label, Reading reading)
    {
        var plain = Plain(label);
        return Enum.GetValues<LabelStyle>().Cast<LabelStyle?>().FirstOrDefault(s => Plain(Of(s!.Value, reading)) == plain);
    }

    /// True when the label holds this icon (coloured or not).
    public static bool Holds(string label, string iconId) => Parts(label).Any(p => p.Icon == iconId);
}
