namespace OmniControl.App.Features.Volume.Fx;

/// The one list of editor scales the FX window offers (the Scale drop-down and the Options > Scale menu) and how a scale is written.
/// A scale shrinks or grows a whole effect editor: OmniControl's own editors through a layout scale, a plug-in's own window through
/// the plug-in (when it allows it).
public static class EditorScale
{
    /// 100 %: the editor's own size.
    public const int Default = Core.Models.Percent.Max;

    public static IReadOnlyList<int> Percents { get; } = new[] { 60, 70, 80, 90, Default, 125, 150 };

    /// The drop-down's entries, in <see cref="Percents"/> order.
    public static IReadOnlyList<string> Texts { get; } = Percents.Select(TextOf).ToList();

    public static string TextOf(int percent) => $"{percent} %";

    /// A stored percent that is not on the list (an older or hand-edited settings file) counts as <see cref="Default"/>.
    public static int Valid(int percent) => Percents.Contains(percent) ? percent : Default;

    /// The percent of a drop-down entry; <see cref="Default"/> for anything else.
    public static int Parse(string? text)
    {
        for (var i = 0; i < Texts.Count; i++)
            if (Texts[i] == text) return Percents[i];
        return Default;
    }

    /// The factor a layout or a plug-in takes (0.7 for 70 %).
    public static double Factor(int percent) => percent / (double)Core.Models.Percent.Max;
}
