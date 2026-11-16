using System.Globalization;
using System.Text;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>One row's chain in Equalizer APO's config language, limited to one device by its <c>Device:</c> line.</summary>
/// <param name="Text">The section's lines, starting with its comment and its Device line.</param>
/// <param name="Skipped">The enabled effects the APO does not run (kept in the chain, said on the row; a plug-in with its reason).</param>
/// <param name="Plugins">The plug-ins written as <c>VSTPlugin:</c> lines (their delay is not known to OmniControl).</param>
/// <param name="Defaults">Of those, the ones with no saved settings yet: Equalizer APO runs them with the plug-in's defaults.</param>
public sealed record ApoSection(string Text, IReadOnlyList<string> Skipped, IReadOnlyList<string> Plugins, IReadOnlyList<string> Defaults);

/// <summary>
/// The master's and the microphones' chains in Equalizer APO's config language (the user's Equalizer APO and OmniControl APO read
/// the same language); <see cref="ApoConfigFiles"/> writes the file. Only effects the APO can run are written (<see cref="FxEngines.Apo"/>);
/// the others are listed as comments and returned, so the row can say they are not run.
/// </summary>
/// <remarks>
/// Each section starts with <c>Device: {guid}</c>. Equalizer APO (DeviceFilterFactory.cpp at bbfcc3e) matches a word with a brace
/// against the device's connection name, name and GUID, and skips every following command until the next Device line; at the end of an
/// included file it matches again. So each section runs only on its own device (a playback device or a microphone: endpoint GUIDs
/// are unique across both), and the user's own config.txt after the include is unaffected. Without the Device lines every chain
/// would run on every device (and microphone) that has the APO.
/// </remarks>
public static class ApoChainWriter
{
    private const string NewLine = "\r\n";

    /// <summary>The whole file: a header and every section, in order.</summary>
    public static string File(IEnumerable<ApoSection> sections)
    {
        var text = new StringBuilder("# OmniControl effects (written by OmniControl; edits here are overwritten)" + NewLine);
        foreach (var section in sections) text.Append(section.Text);
        return text.ToString();
    }

    /// <summary>The section for <paramref name="slots"/> on the device <paramref name="deviceGuid"/> ("{...}"), named <paramref name="title"/> in a comment.</summary>
    public static ApoSection Section(string title, IEnumerable<FxSlot> slots, string deviceGuid)
    {
        var text = new StringBuilder($"# {Clean(title)}{NewLine}Device: {deviceGuid}{NewLine}");
        var notRun = new List<string>();
        var plugins = new List<string>();
        var defaults = new List<string>();
        foreach (var slot in slots.Where(s => s.Enabled))
        {
            string? reason = null;
            IReadOnlyList<string>? lines = null;
            if (FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Apo) && slot.Kind != FxCatalog.Vst2Kind) lines = LinesOf(slot);
            else if (FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Apo) && PluginLine(slot, out reason, out var withDefaults) is { } line)
            {
                lines = new[] { line };
                plugins.Add(slot.Name);
                if (withDefaults) defaults.Add(slot.Name);
            }
            if (lines is null)
            {
                notRun.Add(reason is null ? slot.Name : $"{slot.Name} ({reason})");
                text.Append($"# {Clean(slot.Name)}: not run by the APO{NewLine}");
                continue;
            }
            text.Append($"# {Clean(slot.Name)}{NewLine}");
            foreach (var l in lines) text.Append(l).Append(NewLine);
        }
        return new ApoSection(text.ToString(), notRun, plugins, defaults);
    }

    /// <summary>
    /// A VST2 plug-in as Equalizer APO's <c>VSTPlugin:</c> command (VSTPluginFilterFactory.cpp at bbfcc3e): the full path of its .dll,
    /// then its saved settings, the program chunk as <c>ChunkData</c> or each parameter's name and value (Equalizer APO finds parameters
    /// by name). Null, with the reason, when Equalizer APO can't run it: a wet mix below 100 %, a missing file, or a plug-in without the
    /// entry point Equalizer APO calls (VSTPluginMain). <paramref name="withDefaults"/>: no settings are saved yet.
    /// </summary>
    private static string? PluginLine(FxSlot slot, out string? reason, out bool withDefaults)
    {
        withDefaults = false;
        reason = !FxCatalog.ApoCanMix(slot) ? null // said like every other effect with a wet mix below 100 %
            : !System.IO.File.Exists(slot.Path) ? "its file is missing"
            : !Vst2File.HasApoEntry(slot.Path) ? "Equalizer APO can't start this plug-in"
            : null;
        if (!FxCatalog.ApoCanMix(slot) || reason is not null) return null;
        var line = new StringBuilder($"VSTPlugin: Library {Quoted(slot.Path)}");
        switch (SettingsOf(slot))
        {
            case { Chunk: { } chunk }:
                line.Append(" ChunkData ").Append(Quoted(Convert.ToBase64String(chunk)));
                break;
            case { } settings:
                foreach (var (name, value) in settings.Values.Where(v => IsWritableName(v.Name)))
                    line.Append(' ').Append(Quoted(name)).Append(' ').Append(value.ToString("0.#########", CultureInfo.InvariantCulture));
                break;
            default:
                withDefaults = true;
                break;
        }
        return line.ToString();
    }

    private static Vst2Settings? SettingsOf(FxSlot slot)
    {
        if (slot.State.Length == 0) return null;
        try
        {
            return Vst2Settings.Read(Convert.FromBase64String(slot.State));
        }
        catch (FormatException)
        {
            return null; // damaged: the plug-in starts from its defaults, as in OmniControl's own engine
        }
    }

    /// <summary>A name Equalizer APO can match: not empty (an empty part is dropped when the line is read) and on one line.</summary>
    private static bool IsWritableName(string name) => name.Length > 0 && !name.Contains('\r') && !name.Contains('\n');

    /// <summary>A part in quotes, as Equalizer APO splits a line (StringHelper::splitQuoted): a quote inside is written twice.</summary>
    private static string Quoted(string part) => $"\"{part.Replace("\"", "\"\"")}\"";

    /// <summary>Lines for one slot, or null when this slot cannot be expressed (a wet mix below 100 %, an IR mixed below 100 % or missing).</summary>
    private static IReadOnlyList<string>? LinesOf(FxSlot slot)
    {
        if (!FxCatalog.ApoCanMix(slot)) return null;
        if (FxCatalog.EqLayoutOf(slot) is { } eq) // every equaliser, parametric or graphic: its bands are ordinary bands
            return EqLines((slot.Bands ?? eq.Default()).Where(b => !EqMath.IsFlat(b)).ToList(), FxCatalog.EqGainOf(slot));
        switch (slot.Kind)
        {
            case FxCatalog.GainKind:
                return new[] { $"Preamp: {Num(slot.Values.FirstOrDefault())} dB" };
            case FxCatalog.IrKind:
                if (IrParams.ValueOf(slot, IrParams.Mix) < Core.Models.Percent.Max || !System.IO.File.Exists(slot.Path)) return null; // its own Mix below 100 % cannot be expressed either
                return new[] { $"Preamp: {Num(IrParams.ValueOf(slot, IrParams.Gain) + AutoLevelDb(slot))} dB", $"Convolution: {slot.Path}" };
            default:
                return null;
        }
    }

    /// <summary>
    /// The gain Auto level adds to an IR Loader (the loudest frequency of the file at 0 dB, as in OmniControl's engine), 0 when it is off or
    /// the file cannot be read (Equalizer APO then plays the file as it is). Trim silence is not written: Equalizer APO's Convolution has no such option.
    /// </summary>
    private static double AutoLevelDb(FxSlot slot)
    {
        if (!IrParams.IsOn(IrParams.ValueOf(slot, IrParams.AutoLevel))) return 0;
        try
        {
            var audio = Ir.IrWaveFile.Read(slot.Path);
            var peak = Math.Max(Ir.IrPictures.PeakResponse(audio.Channels[0]), audio.Channels.Length > 1 ? Ir.IrPictures.PeakResponse(audio.Channels[1]) : 0);
            return peak > 1e-9 ? Math.Round(-20 * Math.Log10(peak), 2) : 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return 0; // not a WAV this reader takes (a .flac, say): Equalizer APO reads it as it is
        }
    }

    /// <summary>
    /// The bands that change the sound, after one Preamp: the EQ's overall gain, minus the bands' highest boost (Equalizer APO's own
    /// advice): the APO has no limiter, so without it a boost would clip in the device. Cuts need no headroom.
    /// </summary>
    private static List<string> EqLines(IReadOnlyList<EqBand> bands, double gainDb)
    {
        var lines = bands.Select(Band).ToList();
        var peak = Math.Round(EqMath.PeakDb(bands), 2); // rounding drops float noise around 0 dB when every band cuts
        var preamp = gainDb - Math.Max(0, peak);
        if (preamp != 0) lines.Insert(0, $"Preamp: {Num(preamp)} dB");
        return lines;
    }

    /// <summary>
    /// One band in Equalizer APO's filter syntax, with the same Q the engine uses. LPQ/HPQ are the pass types that take a Q; the
    /// types that ignore gain (<see cref="EqMath.UsesGain"/>) get no Gain, as the APO ignores it too.
    /// </summary>
    private static string Band(EqBand b)
    {
        var type = b.Type switch
        {
            EqType.LowShelf => "LSC",
            EqType.HighShelf => "HSC",
            EqType.LowPass => "LPQ",
            EqType.HighPass => "HPQ",
            EqType.BandPass => "BP",
            EqType.Notch => "NO",
            EqType.AllPass => "AP",
            _ => "PK",
        };
        var gain = EqMath.UsesGain(b.Type) ? $" Gain {Num(b.Gain)} dB" : string.Empty;
        return $"Filter: ON {type} Fc {Freq(b.Freq)} Hz{gain} Q {Num(b.Q)}";
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>At most two decimals: Equalizer APO reads a frequency with a point four from the end ("100.123") as a thousands separator.</summary>
    private static string Freq(double hz) => hz.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Names go into comment lines only; a line break in a name must not start a config command.</summary>
    private static string Clean(string name) => name.Replace('\r', ' ').Replace('\n', ' ');
}
