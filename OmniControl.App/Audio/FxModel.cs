namespace OmniControl.App.Audio;

/// <summary>One effect in a chain. Plain data, saved in mixer.json; the effects engine reads it (see <see cref="FxKind.Engines"/>).</summary>
public sealed class FxSlot
{
    /// <summary>Which effect: a <see cref="FxCatalog"/> kind ("eq", "comp", ...) or a plug-in type's kind (<see cref="FxCatalog.PluginTypes"/>) for a plug-in file.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    /// <summary>A new effect is fully wet.</summary>
    public const int DefaultWet = Core.Models.Percent.Max;

    /// <summary>Wet/dry mix, 0-100 percent.</summary>
    public int Wet { get; set; } = DefaultWet;
    /// <summary>Parameter values in the order of the catalog entry.</summary>
    public List<double> Values { get; set; } = new();
    /// <summary>The bands of an equaliser slot (any number up to <see cref="Eq.MaxBands"/>).</summary>
    public List<EqBand>? Bands { get; set; }
    /// <summary>The plug-in file (a plug-in kind) or the impulse response file (kind "ir"); empty otherwise.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>A plug-in's own settings (base64 of its saved state); empty until the plug-in has run or been edited.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>An independent copy (copy and paste in the FX window): a new slot, so the engine and the plug-in host treat it as a new effect.</summary>
    public FxSlot Copy() => new()
    {
        Kind = Kind, Name = Name, Enabled = Enabled, Wet = Wet, Values = new List<double>(Values),
        Bands = Bands?.Select(b => b.Copy()).ToList(), Path = Path, State = State,
    };
}

/// <summary>Which effects engines can run an effect kind.</summary>
[Flags]
public enum FxEngines
{
    None = 0,
    /// <summary>OmniControl's own engine (programs in Fast mode; everything in Full control).</summary>
    Program = 1,
    /// <summary>Equalizer APO or OmniControl APO (the master and microphones in Fast mode).</summary>
    Apo = 2,
}

public sealed record FxParam(string Name, double Default, double Min, double Max, string Unit);

/// <summary>A plug-in format OmniControl can put in a chain (an entry of <see cref="FxCatalog.PluginTypes"/>).</summary>
/// <param name="Kind">The stored id (<see cref="FxSlot.Kind"/>); never renamed.</param>
/// <param name="Tag">The short name shown before a plug-in's name in the chain and the Add effect window ("VST3"); also the format stored in plugins.json.</param>
/// <param name="Extension">The file name ending of this format's plug-ins (".vst3": a file or a bundle folder; ".dll").</param>
/// <param name="Format">How Core loads it.</param>
/// <param name="Engines">The engines that run it.</param>
public sealed record PluginType(string Kind, string Tag, string Extension, Core.Engine.PluginFormat Format, FxEngines Engines)
{
    /// <summary>What it is, said on a chain row's tooltip.</summary>
    public string Description => $"{Tag} plug-in";
}

/// <param name="Kind">The stored id (<see cref="FxSlot.Kind"/>); never renamed.</param>
/// <param name="Name">The name shown everywhere (Add effect, the chain, the APO file's comments); a built-in slot takes it from here on load.</param>
/// <param name="Engines">The engines that process this effect; elsewhere it is kept in the chain but passes the sound unchanged.</param>
/// <param name="Eq">For an equaliser, how it makes and loads its bands (its slot holds <see cref="FxSlot.Bands"/>); null for the other effects.</param>
public sealed record FxKind(string Kind, string Name, string Description, IReadOnlyList<FxParam> Params, FxEngines Engines = FxEngines.None,
    EqLayout? Eq = null);

/// <summary>
/// The built-in effects the Add effect window offers. The parameter lists follow TabForge's built-ins (same names, ranges and defaults);
/// new parameters are appended so values saved earlier keep their place. Add an entry here to offer a new effect.
/// </summary>
public static class FxCatalog
{
    /// <summary>The stored kind of a VST3 plug-in (from before VST2 existed in OmniControl, hence the plain name; never renamed).</summary>
    public const string Vst3Kind = "vst";

    public const string Vst2Kind = "vst2";

    /// <summary>
    /// The plug-in formats a chain can hold. VST3 runs in OmniControl's own engine only (Equalizer APO has no VST3 host: in Fast mode it is
    /// kept but not run on the master and microphones); VST2 also runs in Equalizer APO (its <c>VSTPlugin:</c> command, <see cref="Engine.ApoChainWriter"/>).
    /// </summary>
    public static readonly IReadOnlyList<PluginType> PluginTypes = new PluginType[]
    {
        new(Vst3Kind, "VST3", ".vst3", Core.Engine.PluginFormat.Vst3, FxEngines.Program),
        new(Vst2Kind, "VST2", ".dll", Core.Engine.PluginFormat.Vst2, FxEngines.Program | FxEngines.Apo),
    };

    /// <summary>The slot's plug-in type; null for a built-in effect.</summary>
    public static PluginType? PluginTypeOf(FxSlot slot) => PluginTypes.FirstOrDefault(t => t.Kind == slot.Kind);

    /// <summary>The plug-in type with this tag ("VST3", as plugins.json stores it); null for none.</summary>
    public static PluginType? PluginTypeByTag(string tag) => PluginTypes.FirstOrDefault(t => t.Tag == tag);

    public static bool IsPlugin(FxSlot slot) => PluginTypeOf(slot) is not null;

    private static FxParam Percent(string name, double def, double max = Core.Models.Percent.Max) => new(name, def, Core.Models.Percent.Min, max, " %");

    public const string IrKind = "ir";
    public const string GainKind = "gain";

    /// <summary>The Parametric equaliser (the one equaliser of older versions, so old files convert to it).</summary>
    public const string EqKind = "eq";

    /// <summary>Every equaliser's one parameter (Values[0]): a gain after its bands. Equalisers saved before it existed have no value: 0 dB.</summary>
    private static readonly FxParam EqGain = new("Overall gain", 0, Eq.MinGain, Eq.MaxGain, " dB");

    /// <summary>An equaliser's overall gain in dB.</summary>
    public static double EqGainOf(FxSlot slot) => slot.Values.FirstOrDefault();

    /// <summary>The slot's catalog entry; null for a plug-in or a kind this version does not know.</summary>
    public static FxKind? KindOf(FxSlot slot) => All.FirstOrDefault(k => k.Kind == slot.Kind);

    /// <summary>How the slot's bands are made when it is an equaliser (any of them); null otherwise.</summary>
    public static EqLayout? EqLayoutOf(FxSlot slot) => KindOf(slot)?.Eq;

    /// <summary>The engines that can run this slot.</summary>
    public static FxEngines EnginesOf(FxSlot slot) => PluginTypeOf(slot)?.Engines ?? KindOf(slot)?.Engines ?? FxEngines.None;

    /// <summary>Equalizer APO has no dry/wet mix: it runs an effect only at a wet mix of 100 % (the chain writer leaves the others out).</summary>
    public static bool ApoCanMix(FxSlot slot) => slot.Wet == Core.Models.Percent.Max;

    public static readonly IReadOnlyList<FxKind> All = new FxKind[]
    {
        new(IrKind, "IR Loader", "Impulse response (cabinet, room or speaker correction) by convolution", new FxParam[]
        {
            Percent("Mix", 100), new("Gain", 0, -24, 24, " dB"),
        }, FxEngines.Apo),
        new(EqKind, "Equaliser - Parametric", "Parametric equaliser: any number of bands, eight filter types, response graph",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, Eq.Layout),
        new("geq32", "Equaliser - 32 Bands", "Graphic equaliser: 32 sliders a third of an octave apart, 16 Hz to 20 kHz, response curve",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, GraphicEq.ThirtyTwo),
        new("geq16", "Equaliser - 16 Bands", "Graphic equaliser: 16 large sliders two thirds of an octave apart, 20 Hz to 20 kHz, response curve",
            new[] { EqGain }, FxEngines.Program | FxEngines.Apo, GraphicEq.Sixteen),
        new("comp", "Compressor", "Dynamics compressor with soft knee and makeup gain", new FxParam[]
        {
            new("Threshold", -18, -60, 0, " dB"), new("Ratio", 4, 1, 20, " : 1"), new("Attack", 10, 1, 200, " ms"),
            new("Release", 120, 5, 2000, " ms"), new("Makeup", 0, 0, 24, " dB"), new("Knee", 6, 0, 24, " dB"),
        }),
        new("limiter", "Limiter", "Look-ahead limiter that stops peaks going over a ceiling", new FxParam[]
        {
            new("Ceiling", -1, -12, 0, " dB"), new("Release", 100, 1, 1000, " ms"), new("Input gain", 0, -12, 24, " dB"),
        }),
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

    /// <summary>A slot for a plug-in file of the given type.</summary>
    public static FxSlot CreatePlugin(PluginType type, string name, string path) => new() { Kind = type.Kind, Name = name, Path = path };
}
