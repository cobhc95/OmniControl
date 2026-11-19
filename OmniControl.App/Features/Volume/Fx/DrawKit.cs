using System.Globalization;

namespace OmniControl.App.Features.Volume.Fx;

/// What the FX window's drawn controls share for showing values: their text sizes, a theme brush by key and a line of text (the
/// Parametric's <see cref="EqGraph"/>, the graphic equaliser's curve, dB scale and band labels, the total response graphs), and the
/// frequency and gain texts every equaliser card and graph readout shows.
internal static class DrawKit
{
    /// Scale and band labels, and the one-line readout of the band being changed.
    public const double LabelSize = 11, InfoSize = 12;

    /// The scale mark inside a small graph (the FX window's 34 px response thumbnail).
    public const double SmallLabelSize = 9;

    /// A brush of the current theme (Styles/Brushes.axaml), or transparent while it is not found.
    public static IBrush Brush(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Transparent;

    public static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    /// A frequency as a scale shows it: "20", "31.5", "1k", "1.25k", "16k".
    public static string ShortFreq(double hz) => hz >= 1000 ? $"{hz / 1000:0.##}k" : $"{hz:0.#}";

    /// A frequency with its unit: "31.5 Hz", "1.25 kHz".
    public static string Freq(double hz) => hz >= 1000 ? $"{hz / 1000:0.##} kHz" : $"{hz:0.#} Hz";

    /// A gain with its sign and one to two decimals (a typed 7.25 stays 7.25), no unit: "+3.5", "0.0", "-6.0", "+7.25".
    public static string SignedDb(double db) => db > 0 ? $"+{db:0.0#}" : $"{db:0.0#}";

    /// A frequency read off a graph, three figures: "52.3 Hz", "250 Hz", "1.00 kHz", "12.5 kHz".
    public static string ReadoutFreq(double hz) => hz switch
    {
        >= 10000 => $"{hz / 1000:0.0} kHz",
        >= 1000 => $"{hz / 1000:0.00} kHz",
        >= 100 => $"{hz:0} Hz",
        _ => $"{hz:0.0} Hz",
    };

    /// A gain read off a graph: "+24.0 dB", "-0.3 dB" (a value that rounds to zero reads "0.0 dB").
    public static string ReadoutDb(double db) => $"{SignedDb(Math.Round(db, 2) == 0 ? 0 : db)} dB";
}
