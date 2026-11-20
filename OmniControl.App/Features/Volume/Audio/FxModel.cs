namespace OmniControl.App.Features.Volume.Audio;

/// One effect in a chain. Plain data, saved in mixer.json; the effects engine reads it (see <see cref="FxKind.Engines"/>).
public sealed class FxSlot
{
    /// Which effect: a <see cref="FxCatalog"/> kind ("eq", "comp", ...) or a plug-in type's kind (<see cref="FxCatalog.PluginTypes"/>) for a plug-in file.
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// Whether a row's chain has something to run: its effects are switched on and at least one effect in it is enabled.
    public static bool AnyRuns(bool on, IEnumerable<FxSlot> chain) => on && chain.Any(s => s.Enabled && !s.IsFolder);

    /// How many effects a chain holds (the count on the Volume list): device folders are not effects.
    public static int EffectCount(IEnumerable<FxSlot> chain) => chain.Count(s => !s.IsFolder);

    /// A device folder (<see cref="FxFolders"/>): not an effect; the effects after it, up to the next folder, are its own.
    public bool IsFolder => Kind == FxFolders.Kind;

    /// A folder's playback device (endpoint id); empty for an effect. <see cref="Name"/> keeps the device's name for when it is unplugged.
    public string DeviceId { get; set; } = string.Empty;

    /// A new effect is fully wet.
    public const int DefaultWet = Core.Models.Percent.Max;

    /// Wet/dry mix, 0-100 percent.
    public int Wet { get; set; } = DefaultWet;
    /// Parameter values in the order of the catalog entry.
    public List<double> Values { get; set; } = new();
    /// The bands of an equaliser slot (any number up to <see cref="Eq.MaxBands"/>).
    public List<EqBand>? Bands { get; set; }
    /// The plug-in file (a plug-in kind) or the impulse response file (kind "ir"); empty otherwise.
    public string Path { get; set; } = string.Empty;
    /// A plug-in's own settings (base64 of its saved state); empty until the plug-in has run or been edited.
    public string State { get; set; } = string.Empty;
    /// What the user had tuned by hand (an equaliser's bands, the compressor's values) before choosing a preset over it, so "Custom" in the
    /// preset list brings it back. Null until a preset first replaced a hand-tuned sound; older files have none.
    public PresetContent? Custom { get; set; }

    /// An independent copy (copy and paste in the FX window): a new slot, so the engine and the plug-in host treat it as a new effect.
    public FxSlot Copy() => new()
    {
        Kind = Kind, Name = Name, DeviceId = DeviceId, Enabled = Enabled, Wet = Wet, Values = new List<double>(Values),
        Bands = Bands?.Select(b => b.Copy()).ToList(), Path = Path, State = State, Custom = Custom?.Copy(),
    };

    /// Takes every setting of <paramref name="from"/> (undo and redo in the FX window), staying the same effect for the engine.
    public void Assign(FxSlot from)
    {
        var copy = from.Copy();
        (Kind, Name, DeviceId, Enabled, Wet, Values, Bands, Path, State, Custom) =
            (copy.Kind, copy.Name, copy.DeviceId, copy.Enabled, copy.Wet, copy.Values, copy.Bands, copy.Path, copy.State, copy.Custom);
    }
}

/// Which effects engines can run an effect kind.
[Flags]
public enum FxEngines
{
    None = 0,
    /// OmniControl's own engine (every row it runs).
    Program = 1,
    /// The user's Equalizer APO (the master, and a program on a cable).
    Apo = 2,
}

/// <param name="IsSwitch">An on/off setting, stored as 0 or 1 (shown as a switch card, not a slider).</param>
public sealed record FxParam(string Name, double Default, double Min, double Max, string Unit, bool IsSwitch = false);

/// A factory preset of a built-in effect: values for the whole <see cref="FxKind.Params"/> list, in its order.
public sealed record FxPreset(string Name, IReadOnlyList<double> Values);

/// A plug-in format OmniControl can put in a chain (an entry of <see cref="FxCatalog.PluginTypes"/>).
/// <param name="Kind">The stored id (<see cref="FxSlot.Kind"/>); never renamed.</param>
/// <param name="Tag">The short name shown before a plug-in's name in the chain and the Add effect window ("VST3"); also the format stored in plugins.json.</param>
/// <param name="Extension">The file name ending of this format's plug-ins (".vst3": a file or a bundle folder; ".dll").</param>
/// <param name="Format">How Core loads it.</param>
/// <param name="Engines">The engines that run it.</param>
public sealed record PluginType(string Kind, string Tag, string Extension, Core.Engine.PluginFormat Format, FxEngines Engines)
{
    /// What it is, said on a chain row's tooltip.
    public string Description => $"{Tag} plug-in";
}

/// <param name="Kind">The stored id (<see cref="FxSlot.Kind"/>); never renamed.</param>
/// <param name="Name">The name shown everywhere (Add effect, the chain, the APO file's comments); a built-in slot takes it from here on load.</param>
/// <param name="Engines">The engines that process this effect; elsewhere it is kept in the chain but passes the sound unchanged.</param>
/// <param name="SortsFirst">The Add effect window lists it before the other built-in effects (the device folder).</param>
/// <param name="Keywords">Extra words the Add effect window's search finds it by.</param>
/// <param name="Icon">A library icon id (<see cref="Icons.IconLibrary"/>) the Add effect window shows before the name; empty for none.</param>
/// <param name="Eq">For an equaliser, how it makes and loads its bands (its slot holds <see cref="FxSlot.Bands"/>); null for the other effects.</param>
public sealed record FxKind(string Kind, string Name, string Description, IReadOnlyList<FxParam> Params, FxEngines Engines = FxEngines.None,
    EqLayout? Eq = null, IReadOnlyList<FxPreset>? Presets = null, bool SortsFirst = false, string Keywords = "", string Icon = "");

/// The built-in effects the Add effect window offers. The parameter lists follow TabForge's built-ins (same names, ranges and defaults);
/// new parameters are appended so values saved earlier keep their place. Add an entry here to offer a new effect.
public static class FxCatalog
{
    /// The stored kind of a VST3 plug-in (from before VST2 existed in OmniControl, hence the plain name; never renamed).
    public const string Vst3Kind = "vst";

    public const string Vst2Kind = "vst2";

    /// The plug-in formats a chain can hold. VST3 runs in OmniControl's own engine only (Equalizer APO has no VST3 host: there it is
    /// kept but not run on the master and microphones); VST2 also runs in Equalizer APO (its <c>VSTPlugin:</c> command, <see cref="Engine.ApoChainWriter"/>).
    public static readonly IReadOnlyList<PluginType> PluginTypes = new PluginType[]
    {
        new(Vst3Kind, "VST3", ".vst3", Core.Engine.PluginFormat.Vst3, FxEngines.Program),
        new(Vst2Kind, "VST2", ".dll", Core.Engine.PluginFormat.Vst2, FxEngines.Program | FxEngines.Apo),
    };

    /// The slot's plug-in type; null for a built-in effect.
    public static PluginType? PluginTypeOf(FxSlot slot) => PluginTypes.FirstOrDefault(t => t.Kind == slot.Kind);

    /// The plug-in type with this tag ("VST3", as plugins.json stores it); null for none.
    public static PluginType? PluginTypeByTag(string tag) => PluginTypes.FirstOrDefault(t => t.Tag == tag);

    public static bool IsPlugin(FxSlot slot) => PluginTypeOf(slot) is not null;

    private static FxParam Percent(string name, double def, double max = Core.Models.Percent.Max) => new(name, def, Core.Models.Percent.Min, max, " %");

    public const string IrKind = "ir";
    public const string GainKind = "gain";
    public const string CompKind = "comp";
    public const string LimiterKind = "limiter";

    /// The Parametric equaliser (the one equaliser of older versions, so old files convert to it).
    public const string EqKind = "eq";

    /// Every equaliser's one parameter (Values[0]): a gain after its bands. Equalisers saved before it existed have no value: 0 dB.
    private static readonly FxParam EqGain = new("Overall gain", 0, Eq.MinGain, Eq.MaxGain, " dB");

    /// An equaliser's overall gain in dB.
    public static double EqGainOf(FxSlot slot) => slot.Values.FirstOrDefault();

    /// The slot's catalog entry; null for a plug-in or a kind this version does not know.
    public static FxKind? KindOf(FxSlot slot) => All.FirstOrDefault(k => k.Kind == slot.Kind);

    /// How the slot's bands are made when it is an equaliser (any of them); null otherwise.
    public static EqLayout? EqLayoutOf(FxSlot slot) => KindOf(slot)?.Eq;

    /// The engines that can run this slot.
    public static FxEngines EnginesOf(FxSlot slot) => PluginTypeOf(slot)?.Engines ?? KindOf(slot)?.Engines ?? FxEngines.None;

    /// Equalizer APO runs the effect: at a wet mix of 100 %, or below it when that APO blends (<see cref="EffectsRules.ApoBlends"/>, the
    /// chain writer's virtual channels); otherwise the chain writer leaves it out.
    public static bool ApoRuns(FxSlot slot, bool blends) => blends || slot.Wet == Core.Models.Percent.Max;

    public static readonly IReadOnlyList<FxKind> All = new FxKind[]
    {
        new(FxFolders.Kind, "Device folder", "Pick an output device; this chain plays only on it.", Array.Empty<FxParam>(), SortsFirst: true, Keywords: "selector device folder container output", Icon: "device-speaker-plus"),
        new(IrKind, "IR Loader", "Impulse response (cabinet, room or speaker correction) by convolution", new FxParam[]
        {
            Percent("Mix", 100), new("Gain", 0, -24, 24, " dB"),
            new("Auto level", 1, 0, 1, "", IsSwitch: true), new("Trim silence", 0, 0, 1, "", IsSwitch: true),
        }, FxEngines.Program | FxEngines.Apo),
        new(EqKind, "Equaliser - Parametric", "Parametric equaliser: any number of bands, eight filter types, response graph",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, Eq.Layout),
        new("geq32", "Equaliser - 32 Bands", "Graphic equaliser: 32 sliders a third of an octave apart, 16 Hz to 20 kHz, response curve",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, GraphicEq.ThirtyTwo),
        new("geq16", "Equaliser - 16 Bands", "Graphic equaliser: 16 large sliders two thirds of an octave apart, 20 Hz to 20 kHz, response curve",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, GraphicEq.Sixteen),
        new(CompKind, "Compressor", "Dynamics compressor with soft knee and makeup gain", new FxParam[]
        {
            new("Threshold", -18, CompressorParams.MinThreshold, 0, " dB"), new("Ratio", 4, 1, CompressorParams.MaxRatio, " : 1"),
            new("Attack", 10, 1, 200, " ms"), new("Release", 120, 5, 2000, " ms"),
            new("Makeup gain", 0, 0, CompressorParams.MaxMakeup, " dB"), new("Knee", 6, 0, CompressorParams.MaxKnee, " dB"),
            new("Auto makeup", 0, 0, 1, "", IsSwitch: true),
        }, FxEngines.Program, Presets: new FxPreset[]
        {
            // TabForge's factory presets (Bass, Vocal, Guitar), in the order of the parameters above
            new("Bass", new double[] { -22, 4, 25, 120, 3, 6, 0 }),
            new("Vocal", new double[] { -20, 3, 6, 90, 0, 9, 1 }),
            new("Guitar", new double[] { -24, 3, 15, 140, 2, 6, 0 }),
        }),
        new(LimiterKind, "Limiter", "Look-ahead limiter that stops peaks going over a ceiling", new FxParam[]
        {
            new("Ceiling", -1, -12, 0, " dB"), new("Release", 100, 1, 1000, " ms"), new("Input gain", 0, -12, 24, " dB"),
            new("Auto release", 0, 0, 1, "", IsSwitch: true),
        }, FxEngines.Program),
        new("delay", "Delay", "Stereo delay with feedback and filters on the repeats", new FxParam[]
        {
            new("Time", 350, 1, 2000, " ms"), Percent("Feedback", 35, 95), Percent("Mix", 30),
            new("Low cut", 20, 20, 2000, " Hz"), new("High cut", 20000, 1000, 20000, " Hz"),
        }),
        new("gate", "Noise gate", "Silences sound below a level", new FxParam[]
        {
            new("Threshold", -50, -80, 0, " dB"), new("Hold", 50, 0, 500, " ms"),
            new("Attack", 1, 1, 100, " ms"), new("Release", 150, 5, 3000, " ms"), new("Range", -60, -80, 0, " dB"),
        }),
        new("reverb", "Reverb", "Room reverb with size, predelay, filters and width", new FxParam[]
        {
            Percent("Size", 40), new("Predelay", 15, 0, 250, " ms"), Percent("Mix", 30),
            new("High cut", 8000, 1000, 20000, " Hz"), new("Low cut", 20, 20, 2000, " Hz"), Percent("Width", 50),
        }),
        new(GainKind, "Gain", "Louder or quieter", new FxParam[] { new("Gain", 0, -24, 24, " dB") }, FxEngines.Program | FxEngines.Apo),
    };

    public static FxKind Find(string kind) => All.First(k => k.Kind == kind);

    /// Gives a built-in slot a value for every setting its entry lists now: a chain saved before a setting was added keeps its values
    /// and takes the new setting's default. Does nothing for a plug-in or a kind this version does not know.
    public static void Complete(FxSlot slot)
    {
        if (KindOf(slot) is not { } kind) return;
        while (slot.Values.Count < kind.Params.Count) slot.Values.Add(kind.Params[slot.Values.Count].Default);
    }

    public static FxSlot Create(string kind)
    {
        var k = Find(kind);
        return new FxSlot
        {
            Kind = k.Kind,
            Name = k.Name,
            Values = k.Params.Select(p => p.Default).ToList(),
            Bands = k.Eq?.Default(),
        };
    }

    /// A slot for a plug-in file of the given type.
    public static FxSlot CreatePlugin(PluginType type, string name, string path) => new() { Kind = type.Kind, Name = name, Path = path };
}
