using System.Globalization;
using System.Text;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// One row's chain in Equalizer APO's config language, limited to one device by its <c>Device:</c> line.
/// <param name="Text">The section's lines, starting with its comment and its Device line.</param>
/// <param name="Skipped">The enabled effects the APO does not run (kept in the chain, said on the row; a plug-in with its reason).</param>
/// <param name="Plugins">The plug-ins written as <c>VSTPlugin:</c> lines (their delay is not known to OmniControl).</param>
/// <param name="Defaults">Of those, the ones with no saved settings yet: Equalizer APO runs them with the plug-in's defaults.</param>
/// <param name="Waiting">An IR Loader's Auto level is still being measured (<see cref="IrLevels"/>): the section is not written yet.</param>
public sealed record ApoSection(string Text, IReadOnlyList<string> Skipped, IReadOnlyList<string> Plugins, IReadOnlyList<string> Defaults, bool Waiting = false);

/// The master's and the microphones' chains in Equalizer APO's config language (the user's Equalizer APO reads
/// the same language); <see cref="ApoConfigFiles"/> writes the file. Only effects the APO can run are written (<see cref="FxEngines.Apo"/>);
/// the others are listed as comments and returned, so the row can say they are not run. A mix below 100 % (an effect's own, an IR
/// Loader's Mix, a device folder's Mix) is the same blend as in OmniControl's own engine, written with virtual channels and Copy lines.
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

    /// The whole file: a header and every section, in order.
    public static string File(IEnumerable<ApoSection> sections)
    {
        var text = new StringBuilder("# OmniControl effects (written by OmniControl; edits here are overwritten)" + NewLine);
        foreach (var section in sections) text.Append(section.Text);
        return text.ToString();
    }

    /// The section for <paramref name="parts"/> (<see cref="FxFolders.PartsFor"/>: the effects outside every folder, then each folder
    /// that runs on this device) on the device <paramref name="deviceGuid"/> ("{...}"), named <paramref name="title"/> in a comment.
    /// <param name="channels">The device's channel count: a mix below 100 % keeps a dry copy of each (<see cref="SectionText.Blend"/>).</param>
    /// <param name="blends">The APO that reads this file blends (<see cref="Core.Engine.Apo.ApoState.Blends"/>); when not, an effect
    /// or folder below 100 % is left out and named.</param>
    /// <param name="levels">The IR Loaders' Auto level gains, measured off the UI thread.</param>
    internal static ApoSection Section(string title, IEnumerable<FxPart> parts, string deviceGuid, int channels, bool blends, IrLevels levels)
    {
        var section = new SectionText(Math.Max(1, channels));
        section.Text.Append($"# {Clean(title)}{NewLine}Device: {deviceGuid}{NewLine}");
        foreach (var part in parts)
        {
            var mix = part.Folder is { } folder ? Core.Models.Percent.ToScalar(folder.Wet) : 1;
            if (mix < 1 && !blends)
            {
                foreach (var slot in part.Effects.Where(s => s.Enabled)) section.NotRun(slot, EffectsRules.ApoNoBlendReason);
                continue;
            }
            var effects = new StringBuilder();
            foreach (var slot in part.Effects.Where(s => s.Enabled)) Effect(section, effects, slot, blends, levels);
            if (effects.Length == 0) continue;
            section.Text.Append(mix < 1 ? section.Blend($"Device folder {part.Folder!.Name}", mix, effects.ToString()) : effects.ToString());
        }
        return new ApoSection(section.Text.ToString(), section.Skipped, section.Plugins, section.Defaults, section.Waiting);
    }

    /// One enabled effect's lines into <paramref name="into"/>, inside its own blend when its mix is below 100 %; or why it is not run.
    private static void Effect(SectionText section, StringBuilder into, FxSlot slot, bool blends, IrLevels levels)
    {
        if (!FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Apo))
        {
            section.NotRun(slot, null);
            return;
        }
        var mix = MixOf(slot);
        if (mix < 1 && !blends)
        {
            section.NotRun(slot, EffectsRules.ApoNoBlendReason);
            return;
        }
        IReadOnlyList<string>? lines;
        string? reason = null;
        if (slot.Kind != FxCatalog.Vst2Kind)
        {
            lines = LinesOf(slot, levels, out var measuring);
            section.Waiting |= measuring;
        }
        else if (PluginLine(slot, out reason, out var withDefaults) is { } line)
        {
            lines = new[] { line };
            section.Plugins.Add(slot.Name);
            if (withDefaults) section.Defaults.Add(slot.Name);
        }
        else lines = null;
        if (lines is null)
        {
            section.NotRun(slot, reason);
            return;
        }
        var text = new StringBuilder($"# {Clean(slot.Name)}{NewLine}");
        foreach (var l in lines) text.Append(l).Append(NewLine);
        into.Append(mix < 1 ? section.Blend(slot.Name, mix, text.ToString()) : text.ToString());
    }

    /// The effect's own mix (0-1): its wet mix, and for an IR Loader its Mix as well (the built-in engine applies both in turn).
    private static double MixOf(FxSlot slot) =>
        Core.Models.Percent.ToScalar(slot.Wet) * (slot.Kind == FxCatalog.IrKind ? IrParams.ValueOf(slot, IrParams.Mix) / Core.Models.Percent.Max : 1);

    /// The start of a blend's virtual channel names ("OCDRY1_2" = blend 1's dry copy of channel 2); never a channel a device has.
    private const string DryPrefix = "OCDRY";

    /// One section's text while it is written, what it leaves out, and its blends' numbering (each blend's dry copies are virtual
    /// channels with their own names, so blends can sit inside each other: a folder's around an effect's).
    private sealed class SectionText(int channels)
    {
        private int _blends;

        public StringBuilder Text { get; } = new();
        public List<string> Skipped { get; } = new();
        public List<string> Plugins { get; } = new();
        public List<string> Defaults { get; } = new();
        public bool Waiting { get; set; }

        public void NotRun(FxSlot slot, string? reason)
        {
            Skipped.Add(reason is null ? slot.Name : $"{slot.Name} ({reason})");
            Text.Append($"# {Clean(slot.Name)}: not run by the APO{NewLine}");
        }

        /// <paramref name="lines"/> blended with the sound before them: dry x (1 - mix) + processed x mix, per channel. A Copy keeps each
        /// channel in a new virtual channel (a name the device does not have: Equalizer APO adds it and never plays it), the lines run on the
        /// device's channels only (a Copy leaves the channel selection as it was), then a Copy mixes each channel back with its dry copy.
        /// Channels are named by number, so any layout works.
        public string Blend(string name, double mix, string lines)
        {
            var id = ++_blends;
            string Dry(int channel) => $"{DryPrefix}{id}_{channel}";
            var all = Enumerable.Range(1, channels).ToList();
            return $"# {Clean(name)}: mix {Num(mix * Core.Models.Percent.Max)} %, blended with the sound before it{NewLine}"
                + $"Copy: {string.Join(' ', all.Select(c => $"{Dry(c)}={c}"))}{NewLine}"
                + lines
                + $"Copy: {string.Join(' ', all.Select(c => $"{c}={Num(1 - mix)}*{Dry(c)}+{Num(mix)}*{c}"))}{NewLine}";
        }
    }

    /// A VST2 plug-in as Equalizer APO's <c>VSTPlugin:</c> command (VSTPluginFilterFactory.cpp at bbfcc3e): the full path of its .dll,
    /// then its saved settings, the program chunk as <c>ChunkData</c> or each parameter's name and value (Equalizer APO finds parameters
    /// by name). Null, with the reason, when Equalizer APO can't run it: a missing file, or a plug-in without the entry point Equalizer
    /// APO calls (VSTPluginMain). <paramref name="withDefaults"/>: no settings are saved yet.
    private static string? PluginLine(FxSlot slot, out string? reason, out bool withDefaults)
    {
        withDefaults = false;
        reason = !System.IO.File.Exists(slot.Path) ? "its file is missing"
            : !Vst2File.HasApoEntry(slot.Path) ? "Equalizer APO can't start this plug-in"
            : null;
        if (reason is not null) return null;
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

    /// A name Equalizer APO can match: not empty (an empty part is dropped when the line is read) and on one line.
    private static bool IsWritableName(string name) => name.Length > 0 && !name.Contains('\r') && !name.Contains('\n');

    /// A part in quotes, as Equalizer APO splits a line (StringHelper::splitQuoted): a quote inside is written twice.
    private static string Quoted(string part) => $"\"{part.Replace("\"", "\"\"")}\"";

    /// Lines for one slot (its mix is the caller's), or null when this slot cannot be expressed (an unknown kind, an IR file that is missing).
    /// <param name="waiting">An IR file's Auto level is not measured yet.</param>
    private static IReadOnlyList<string>? LinesOf(FxSlot slot, IrLevels levels, out bool waiting)
    {
        waiting = false;
        if (FxCatalog.EqLayoutOf(slot) is { } eq) // every equaliser, parametric or graphic: its bands are ordinary bands
            return EqLines((slot.Bands ?? eq.Default()).Where(b => !EqMath.IsFlat(b)).ToList(), FxCatalog.EqGainOf(slot));
        switch (slot.Kind)
        {
            case FxCatalog.GainKind:
                return new[] { $"Preamp: {Num(slot.Values.FirstOrDefault())} dB" };
            case FxCatalog.IrKind:
                var stamp = Ir.IrFileStamp.Of(slot.Path);
                if (stamp == 0) return null;
                var level = IrParams.IsOn(IrParams.ValueOf(slot, IrParams.AutoLevel)) ? levels.Of(slot.Path, stamp) : 0;
                if (level is not { } autoDb)
                {
                    waiting = true;
                    return Array.Empty<string>();
                }
                // The file's write time is part of the text: a file replaced under the same name changes it, so Equalizer APO loads it again.
                // Trim silence is not written: Equalizer APO's Convolution has no such option.
                return new[] { $"# file saved {new DateTime(stamp, DateTimeKind.Utc):O}", $"Preamp: {Num(IrParams.ValueOf(slot, IrParams.Gain) + autoDb)} dB", $"Convolution: {slot.Path}" };
            default:
                return null;
        }
    }

    /// The bands that change the sound, after one Preamp: the EQ's overall gain, minus the bands' highest boost (Equalizer APO's own
    /// advice): the APO has no limiter, so without it a boost would clip in the device. Cuts need no headroom.
    private static List<string> EqLines(IReadOnlyList<EqBand> bands, double gainDb)
    {
        var lines = bands.Select(Band).ToList();
        var peak = Math.Round(EqMath.PeakDb(bands), 2); // rounding drops float noise around 0 dB when every band cuts
        var preamp = gainDb - Math.Max(0, peak);
        if (preamp != 0) lines.Insert(0, $"Preamp: {Num(preamp)} dB");
        return lines;
    }

    /// One band in Equalizer APO's filter syntax, with the same Q the engine uses. LPQ/HPQ are the pass types that take a Q; the
    /// types that ignore gain (<see cref="EqMath.UsesGain"/>) get no Gain, as the APO ignores it too.
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

    /// At most two decimals: Equalizer APO reads a frequency with a point four from the end ("100.123") as a thousands separator.
    private static string Freq(double hz) => hz.ToString("0.##", CultureInfo.InvariantCulture);

    /// Names go into comment lines only; a line break in a name must not start a config command.
    private static string Clean(string name) => name.Replace('\r', ' ').Replace('\n', ' ');
}
