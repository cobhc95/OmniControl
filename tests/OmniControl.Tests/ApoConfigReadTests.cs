using System.Text;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.Tests;

/// The folder watch's line-by-line reading of config.txt gives what reading the whole file did (the reference below), for every shape.
public sealed class ApoConfigReadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("omni-apo").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Chain = "Device: x\r\nPreamp: -3 dB\r\n";
    private static readonly string Bom = Encoding.Latin1.GetString(Encoding.UTF8.Preamble);
    private static readonly string M = ApoIds.IncludeMarker, I = ApoIds.IncludeLine;

    public static TheoryData<string?> Configs() => new()
    {
        null,
        "",
        $"{M}\r\n{I}\r\n",
        $"{Bom}{M}\r\n{I}\r\n",
        $"{M}\r\n{I}\r\nPreamp: 2 dB\r\n# note\r\n",
        $"{M}\r\n{I}\r\nPreamp: 2 dB\r\n{I}\r\n",
        $"Preamp: 2 dB\r\n{M}\r\n{I}\r\n",
        $"{M}\n{I}\n",
        $"{M}\r\n{I}",
        $"  {M}\r\n{I}\r\n",
        $"{ApoIds.PreviousIncludeMarker}\r\n{I}\r\n",
        "# only a comment\r\n\r\n",
        Bom,
        $"{Bom}Preamp: 1 dB",
        $"{M}\r\n{I}\r\nFilter: ON PK Fc 100 Hz éÿ\r\n",
    };

    [Theory]
    [MemberData(nameof(Configs))]
    public void Reading_line_by_line_matches_reading_the_whole_file(string? config)
    {
        var configPath = Path.Combine(_dir, ApoIds.ConfigFileName);
        if (config is not null) File.WriteAllBytes(configPath, Encoding.Latin1.GetBytes(config));
        var chainPath = Path.Combine(_dir, ApoIds.ChainFileName);
        foreach (var chain in new string?[] { Chain, Chain.Replace('3', '4'), Chain + " ", null })
        {
            if (chain is null) File.Delete(chainPath);
            else File.WriteAllBytes(chainPath, Encoding.UTF8.GetBytes(chain));
            foreach (var wanted in new[] { Chain, null })
                Assert.Equal(Reference(config ?? "", chain, wanted), ApoConfigFiles.Read(_dir, wanted));
        }
    }

    /// The whole-file reading the watch did before.
    private static ApoFolderFacts Reference(string text, string? chain, string? wanted)
    {
        var bom = text.StartsWith(Bom, StringComparison.Ordinal) ? Bom : "";
        var lines = Split(text[bom.Length..]);
        var own = lines.Any(l => !IsOurs(l) && l.Trim() is { Length: > 0 } t && !t.StartsWith('#'));
        var placed = bom + M + "\r\n" + I + "\r\n" + string.Concat(lines.Where(l => !IsOurs(l))) == text;
        var inPlace = wanted is null ? !lines.Any(IsOurs) && chain is null : placed && chain == wanted;
        return new ApoFolderFacts(inPlace, own);
    }

    private static bool IsOurs(string line) =>
        line.Trim().Equals(I, StringComparison.OrdinalIgnoreCase) || line.Trim() == M || line.Trim() == ApoIds.PreviousIncludeMarker;

    private static List<string> Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') { lines.Add(text[start..(i + 1)]); start = i + 1; }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }
}
