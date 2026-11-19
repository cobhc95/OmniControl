using System.Text.Json;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// The .omnifx file of the FX window's "Save chain…" / "Load chain…": one json file holding the
/// whole chain, the same <see cref="FxSlot"/> list the settings keep (effects with every setting, device folders and their Mix).
public static class FxChainFile
{
    public const string Extension = ".omnifx";
    public const string TypeName = "OmniControl effect chain";
    public static readonly IReadOnlyList<string> Patterns = new[] { "*" + Extension };

    private const string FormatId = "omnifx";
    private const string NotAChain = "That file is not an OmniControl effect chain.";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed class Document
    {
        public string Format { get; set; } = string.Empty;
        public List<FxSlot>? Chain { get; set; }
    }

    /// Writes the chain to <paramref name="path"/> (a temp file first, then moved over, so a failed save never leaves half a file).
    public static void Write(string path, IReadOnlyList<FxSlot> chain)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Document { Format = FormatId, Chain = chain.ToList() }, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// The chain in the file, or null with a plain sentence in <paramref name="problem"/> when it is not a chain OmniControl can load.
    public static List<FxSlot>? Read(string path, out string? problem)
    {
        problem = null;
        try
        {
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (doc is { Chain: { } chain } && doc.Format == FormatId && chain.All(IsKnown)) return chain;
            problem = NotAChain;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = ex is JsonException ? NotAChain : "Couldn't read that file.";
        }
        return null;
    }

    private static bool IsKnown(FxSlot slot) =>
        slot.IsFolder || FxCatalog.KindOf(slot) is not null || FxCatalog.IsPlugin(slot);
}
