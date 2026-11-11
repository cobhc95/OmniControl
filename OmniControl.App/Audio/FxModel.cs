namespace OmniControl.App.Audio;

/// <summary>One effect in a chain. Plain data, saved in mixer.json. No sound engine reads it yet.</summary>
public sealed class FxSlot
{
    /// <summary>Which effect: a <see cref="FxCatalog"/> kind ("eq", "comp", ...) or <see cref="FxCatalog.PluginKind"/> for a plug-in file.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    /// <summary>Wet/dry mix, 0-100 percent.</summary>
    public int Wet { get; set; } = 100;
    /// <summary>Parameter values in the order of the catalog entry.</summary>
    public List<double> Values { get; set; } = new();
    /// <summary>The 10 bands of an equaliser slot.</summary>
    public List<EqBand>? Bands { get; set; }
    /// <summary>The plug-in file (kind "vst") or the impulse response file (kind "ir"); empty otherwise.</summary>
    public string Path { get; set; } = string.Empty;
}

public sealed record FxParam(string Name, double Default, double Min, double Max, string Unit);

public sealed record FxKind(string Kind, string Name, string Description, IReadOnlyList<FxParam> Params);

/// <summary>
/// The built-in effects the Add effect window offers. The parameter lists follow TabForge's built-ins (same names, ranges and defaults);
/// new parameters are appended so values saved earlier keep their place. Add an entry here to offer a new effect.
/// </summary>
public static class FxCatalog
{
    public const string PluginKind = "vst";
    public const string IrKind = "ir";

    public static readonly IReadOnlyList<FxKind> All = new FxKind[]
    {
        new(IrKind, "IR Loader", "Impulse response (cabinet, room or speaker correction) by convolution", new FxParam[]
        {
            new("Mix", 100, 0, 100, " %"), new("Gain", 0, -24, 24, " dB"),
        }),
        new("eq", "Equaliser", "10-band parametric equaliser with a frequency curve", Array.Empty<FxParam>()),
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
            new("Time", 350, 1, 2000, " ms"), new("Feedback", 35, 0, 95, " %"), new("Mix", 30, 0, 100, " %"),
            new("Low cut", 20, 20, 2000, " Hz"), new("High cut", 20000, 1000, 20000, " Hz"),
        }),
        new("gate", "Noise gate", "Silences sound below a level", new FxParam[]
        {
            new("Threshold", -50, -80, 0, " dB"), new("Hold", 50, 0, 500, " ms"),
            new("Attack", 1, 1, 100, " ms"), new("Release", 150, 5, 3000, " ms"), new("Range", -60, -80, 0, " dB"),
        }),
        new("reverb", "Reverb", "Room reverb with size, predelay, filters and width", new FxParam[]
        {
            new("Size", 40, 0, 100, " %"), new("Predelay", 15, 0, 250, " ms"), new("Mix", 30, 0, 100, " %"),
            new("High cut", 8000, 1000, 20000, " Hz"), new("Low cut", 20, 20, 2000, " Hz"), new("Width", 50, 0, 100, " %"),
        }),
        new("gain", "Gain", "Louder or quieter", new FxParam[] { new("Gain", 0, -24, 24, " dB") }),
    };

    /// <summary>The catalog entry for a built-in kind; plug-in slots have none (null).</summary>
    public static FxKind? FindOrNull(string kind) => All.FirstOrDefault(k => k.Kind == kind);

    public static FxKind Find(string kind) => All.First(k => k.Kind == kind);

    public static FxSlot Create(string kind)
    {
        var k = Find(kind);
        return new FxSlot
        {
            Kind = k.Kind,
            Name = k.Name,
            Values = k.Params.Select(p => p.Default).ToList(),
            Bands = kind == "eq" ? Eq.Flat() : null,
        };
    }

    /// <summary>A slot for a plug-in file. It is kept in the chain and saved, but nothing runs it yet.</summary>
    public static FxSlot CreatePlugin(string name, string path) => new() { Kind = PluginKind, Name = name, Path = path };
}
